using System.Collections.Concurrent;
using Zeus.Contracts;
using Zeus.Plugins.Contracts;
using Zeus.Plugins.Midi.Mapping;
using Zeus.Plugins.Midi.Midi;

namespace Zeus.Plugins.Midi.Dispatch;

public sealed class MidiDispatcher
{
    private readonly IRadioCommandSurface _surface;
    private readonly ConcurrentDictionary<string, DeviceMappings> _mappings = new();

    public MidiDispatcher(IRadioCommandSurface surface) => _surface = surface;

    public void SetMapping(string deviceName, MidiMapping mapping)
    {
        var device = _mappings.GetOrAdd(deviceName, _ => new DeviceMappings());
        device.Mappings.RemoveAll(m =>
            m.ControlType == mapping.ControlType &&
            m.Channel == mapping.Channel &&
            m.ControlId == mapping.ControlId);
        device.Mappings.Add(mapping);
    }

    public void LoadMappings(MidiMappingSet set)
    {
        _mappings.Clear();
        foreach (var (name, device) in set.Devices)
            _mappings[name] = device;
    }

    public void HandleEvent(MidiEvent ev)
    {
        if (!_mappings.TryGetValue(ev.DeviceName, out var device)) return;
        var mapping = device.Find(ev.ControlType, ev.Channel, ev.ControlId);
        if (mapping is null) return;

        Dispatch(mapping, ev.Value);
    }

    private void Dispatch(MidiMapping mapping, int value)
    {
        var cmd = mapping.Command;

        if (IsButton(cmd))
        {
            DispatchButton(cmd, mapping);
            return;
        }

        if (IsKnob(cmd))
        {
            DispatchKnob(cmd, value);
            return;
        }

        if (IsWheel(cmd))
        {
            var delta = ValueNormalizer.DecodeDelta(value, mapping.EncoderMode);
            delta *= mapping.StepMultiplier;
            if (delta == 0) return;
            DispatchWheel(cmd, delta);
        }
    }

    private void DispatchButton(ZeusMidiCommand cmd, MidiMapping mapping)
    {
        var state = _surface.Snapshot();

        switch (cmd)
        {
            case ZeusMidiCommand.Mox:
                _surface.TrySetMox(mapping.Toggle ? !state.Status.HasFlag((ConnectionStatus)0) : true, out _);
                break;
            case ZeusMidiCommand.Tune:
                _surface.TrySetTun(true, out _);
                break;
            case ZeusMidiCommand.TwoTone:
                _surface.SetTwoTone(new TwoToneSetRequest(!state.TwoToneEnabled));
                break;
            case ZeusMidiCommand.PureSignal:
                _surface.SetPs(new PsControlSetRequest(!state.PsEnabled, state.PsAuto, state.PsSingle));
                break;

            case ZeusMidiCommand.Nr1:
                ToggleNrMode(state, NrMode.Anr);
                break;
            case ZeusMidiCommand.Nr2:
                ToggleNrMode(state, NrMode.Emnr);
                break;
            case ZeusMidiCommand.Nr3:
                ToggleNrMode(state, NrMode.Sbnr);
                break;
            case ZeusMidiCommand.Nr4:
                _surface.SetNr4(new Nr4ConfigSetRequest());
                break;
            case ZeusMidiCommand.Nb1:
                ToggleNbMode(state, NbMode.Nb1);
                break;
            case ZeusMidiCommand.Nb2:
                ToggleNbMode(state, NbMode.Nb2);
                break;
            case ZeusMidiCommand.Anf:
                var nrAnf = state.Nr ?? new NrConfig();
                _surface.SetNr(nrAnf with { AnfEnabled = !nrAnf.AnfEnabled });
                break;
            case ZeusMidiCommand.Snb:
                var nrSnb = state.Nr ?? new NrConfig();
                _surface.SetNr(nrSnb with { SnbEnabled = !nrSnb.SnbEnabled });
                break;

            case ZeusMidiCommand.AutoAgc:
                _surface.SetAutoAgc(!state.AutoAgcEnabled);
                break;
            case ZeusMidiCommand.AutoAtt:
                _surface.SetAutoAtt(!state.AutoAttEnabled);
                break;
            case ZeusMidiCommand.Preamp:
                _surface.SetPreamp(true);
                break;

            case ZeusMidiCommand.ModeNext:
                _surface.SetMode(NextMode(state.Mode, +1));
                break;
            case ZeusMidiCommand.ModePrev:
                _surface.SetMode(NextMode(state.Mode, -1));
                break;

            case >= ZeusMidiCommand.ModeLsb and <= ZeusMidiCommand.ModeDigu:
                _surface.SetMode((RxMode)(cmd - ZeusMidiCommand.ModeLsb));
                break;

            case ZeusMidiCommand.FilterWider:
                _surface.SetFilter(state.FilterLowHz - 50, state.FilterHighHz + 50);
                break;
            case ZeusMidiCommand.FilterNarrower:
                _surface.SetFilter(state.FilterLowHz + 50, state.FilterHighHz - 50);
                break;

            case ZeusMidiCommand.ZoomIn:
                _surface.SetZoom(state.ZoomLevel + 1);
                break;
            case ZeusMidiCommand.ZoomOut:
                _surface.SetZoom(Math.Max(1, state.ZoomLevel - 1));
                break;

            case ZeusMidiCommand.VfoAUp100k:
                _surface.SetVfo(state.VfoHz + 100_000);
                break;
            case ZeusMidiCommand.VfoADown100k:
                _surface.SetVfo(state.VfoHz - 100_000);
                break;
        }
    }

    private void DispatchKnob(ZeusMidiCommand cmd, int value)
    {
        switch (cmd)
        {
            case ZeusMidiCommand.AfGain:
                _surface.SetRxAfGain(ValueNormalizer.ScaleKnob(value, -50, 20));
                break;
            case ZeusMidiCommand.AgcLevel:
                _surface.SetAgcTop(ValueNormalizer.ScaleKnob(value, -20, 120));
                break;
            case ZeusMidiCommand.Drive:
                _surface.SetDrive(ValueNormalizer.ScaleKnobInt(value, 0, 100));
                break;
            case ZeusMidiCommand.TuneDrive:
                _surface.SetTuneDrive(ValueNormalizer.ScaleKnobInt(value, 0, 100));
                break;
            case ZeusMidiCommand.MicGain:
                _surface.SetTxMicGain(ValueNormalizer.ScaleKnobInt(value, -40, 10));
                break;
            case ZeusMidiCommand.TxMonitor:
                _surface.SetTxMonitor(new TxMonitorSetRequest(value > 0));
                break;
            case ZeusMidiCommand.Nr4Amount:
                _surface.SetNr4(new Nr4ConfigSetRequest(ReductionAmount: ValueNormalizer.ScaleKnob(value, 0, 100)));
                break;
            case ZeusMidiCommand.Zoom:
                _surface.SetZoom(ValueNormalizer.ScaleKnobInt(value, 1, 48));
                break;
            case ZeusMidiCommand.Attenuator:
                _surface.SetAttenuator(ValueNormalizer.ScaleKnobInt(value, 0, 31));
                break;
        }
    }

    private void DispatchWheel(ZeusMidiCommand cmd, int delta)
    {
        var state = _surface.Snapshot();

        switch (cmd)
        {
            case ZeusMidiCommand.VfoATune:
                _surface.SetVfo(state.VfoHz + delta * 10);
                break;
            case ZeusMidiCommand.VfoAMultiStep:
                _surface.SetVfo(state.VfoHz + delta * 1000);
                break;
            case ZeusMidiCommand.FilterBandwidth:
                _surface.SetFilter(state.FilterLowHz - delta * 25, state.FilterHighHz + delta * 25);
                break;
            case ZeusMidiCommand.FilterHigh:
                _surface.SetFilter(state.FilterLowHz, state.FilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.FilterLow:
                _surface.SetFilter(state.FilterLowHz + delta * 10, state.FilterHighHz);
                break;
            case ZeusMidiCommand.FilterShift:
                _surface.SetFilter(state.FilterLowHz + delta * 10, state.FilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.TxFilterHigh:
                _surface.SetTxFilter(state.TxFilterLowHz, state.TxFilterHighHz + delta * 10);
                break;
            case ZeusMidiCommand.TxFilterLow:
                _surface.SetTxFilter(state.TxFilterLowHz + delta * 10, state.TxFilterHighHz);
                break;
            case ZeusMidiCommand.ZoomWheel:
                _surface.SetZoom(Math.Max(1, state.ZoomLevel + delta));
                break;
            case ZeusMidiCommand.AfGainWheel:
                _surface.SetRxAfGain(state.RxAfGainDb + delta * 0.5);
                break;
            case ZeusMidiCommand.AgcLevelWheel:
                _surface.SetAgcTop(state.AgcTopDb + delta);
                break;
            case ZeusMidiCommand.DriveWheel:
                _surface.SetDrive(Math.Clamp(state.DrivePct + delta, 0, 100));
                break;
        }
    }

    private void ToggleNrMode(StateDto state, NrMode target)
    {
        var nr = state.Nr ?? new NrConfig();
        _surface.SetNr(nr with { NrMode = nr.NrMode == target ? NrMode.Off : target });
    }

    private void ToggleNbMode(StateDto state, NbMode target)
    {
        var nr = state.Nr ?? new NrConfig();
        _surface.SetNr(nr with { NbMode = nr.NbMode == target ? NbMode.Off : target });
    }

    private static RxMode NextMode(RxMode current, int direction)
    {
        var values = Enum.GetValues<RxMode>();
        var index = Array.IndexOf(values, current);
        index = (index + direction + values.Length) % values.Length;
        return values[index];
    }

    private static bool IsButton(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.Mox and <= ZeusMidiCommand.Mute;

    private static bool IsKnob(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.AfGain and <= ZeusMidiCommand.Attenuator;

    private static bool IsWheel(ZeusMidiCommand cmd) =>
        cmd is >= ZeusMidiCommand.VfoATune and <= ZeusMidiCommand.DriveWheel;
}
