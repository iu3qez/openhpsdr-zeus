import React, { useState, useEffect, useCallback, useRef } from 'react';

interface ZeusPluginApi {
  registerPanel(spec: { id: string; component: React.ComponentType }): void;
  callBackend(method: string, path: string, body?: unknown): Promise<Response>;
}

interface DeviceInfo {
  name: string;
  isOpen: boolean;
}

interface Mapping {
  controlId: number;
  controlType: number;
  channel: number;
  command: number;
  toggle: boolean;
  relative: boolean;
  encoderMode: number;
  stepMultiplier: number;
}

interface DeviceMappings {
  mappings: Mapping[];
}

interface MappingSet {
  version: number;
  devices: Record<string, DeviceMappings>;
}

interface LearnResult {
  command: number;
  controlType: number;
  channel: number;
  controlId: number;
}

const CONTROL_TYPES = ['CC', 'NoteOn', 'NoteOff', 'PitchBend'] as const;

function MidiSettingsPanel({ api }: { api: ZeusPluginApi }) {
  const [devices, setDevices] = useState<DeviceInfo[]>([]);
  const [selectedDevice, setSelectedDevice] = useState<string>('');
  const [mappings, setMappings] = useState<MappingSet>({ version: 1, devices: {} });
  const [commands, setCommands] = useState<string[]>([]);
  const [learning, setLearning] = useState<string | null>(null);
  const [lastCapture, setLastCapture] = useState<{ controlType: number; channel: number; controlId: number; value: number } | null>(null);
  const [addCommand, setAddCommand] = useState<string>('');
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const fetchDevices = useCallback(async () => {
    try {
      const r = await api.callBackend('GET', '/devices');
      if (r.ok) setDevices(await r.json());
    } catch { /* ignore */ }
  }, [api]);

  const fetchMappings = useCallback(async () => {
    try {
      const r = await api.callBackend('GET', '/mappings');
      if (r.ok) setMappings(await r.json());
    } catch { /* ignore */ }
  }, [api]);

  const fetchCommands = useCallback(async () => {
    try {
      const r = await api.callBackend('GET', '/commands');
      if (r.ok) setCommands(await r.json());
    } catch { /* ignore */ }
  }, [api]);

  useEffect(() => {
    fetchDevices();
    fetchMappings();
    fetchCommands();
    const iv = setInterval(fetchDevices, 3000);
    return () => clearInterval(iv);
  }, [fetchDevices, fetchMappings, fetchCommands]);

  useEffect(() => {
    if (devices.length > 0 && !selectedDevice) {
      setSelectedDevice(devices[0].name);
    }
  }, [devices, selectedDevice]);

  const saveMappings = async (updated: MappingSet) => {
    setMappings(updated);
    await api.callBackend('PUT', '/mappings', updated);
  };

  const deleteMapping = async (idx: number) => {
    if (!selectedDevice) return;
    const dev = mappings.devices[selectedDevice];
    if (!dev) return;
    const updated = {
      ...mappings,
      devices: {
        ...mappings.devices,
        [selectedDevice]: {
          mappings: dev.mappings.filter((_, i) => i !== idx),
        },
      },
    };
    await saveMappings(updated);
  };

  const startLearn = async (commandName: string) => {
    if (!selectedDevice) return;
    setLearning(commandName);
    setLastCapture(null);
    const cmdIdx = commands.indexOf(commandName);
    await api.callBackend('POST', '/learn/start', { deviceName: selectedDevice, command: cmdIdx });

    pollRef.current = setInterval(async () => {
      try {
        const r = await api.callBackend('GET', '/learn/last');
        if (r.ok) {
          const data = await r.json();
          if (data) setLastCapture(data);
        }
      } catch { /* ignore */ }
    }, 200);
  };

  const stopLearn = async (save: boolean) => {
    if (pollRef.current) {
      clearInterval(pollRef.current);
      pollRef.current = null;
    }

    const r = await api.callBackend('POST', '/learn/stop');
    const result: LearnResult | null = r.ok ? await r.json() : null;

    if (save && result && learning && selectedDevice) {
      const existing = mappings.devices[selectedDevice]?.mappings ?? [];
      const filtered = existing.filter(
        (m) => !(m.controlType === result.controlType && m.channel === result.channel && m.controlId === result.controlId),
      );
      const cmdIdx = commands.indexOf(learning);
      const newMapping: Mapping = {
        controlId: result.controlId,
        controlType: result.controlType,
        channel: result.channel,
        command: cmdIdx,
        toggle: false,
        relative: false,
        encoderMode: 0,
        stepMultiplier: 1,
      };
      const updated: MappingSet = {
        ...mappings,
        devices: {
          ...mappings.devices,
          [selectedDevice]: { mappings: [...filtered, newMapping] },
        },
      };
      await saveMappings(updated);
    }

    setLearning(null);
    setLastCapture(null);
  };

  const addMapping = () => {
    if (!addCommand || !selectedDevice) return;
    startLearn(addCommand);
    setAddCommand('');
  };

  const deviceMappings = selectedDevice ? mappings.devices[selectedDevice]?.mappings ?? [] : [];

  return (
    <div style={{ padding: '12px', fontFamily: 'var(--ff-ui, sans-serif)', color: 'var(--fg-0)', fontSize: '13px' }}>
      <h3 style={{ margin: '0 0 12px', fontSize: '15px', fontWeight: 600 }}>MIDI Controllers</h3>

      {/* Device selector */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
        <label style={{ color: 'var(--fg-2)', fontSize: '12px' }}>Device:</label>
        <select
          value={selectedDevice}
          onChange={(e) => setSelectedDevice(e.target.value)}
          style={{
            flex: 1,
            background: 'var(--bg-1)',
            color: 'var(--fg-0)',
            border: '1px solid var(--border-0)',
            borderRadius: 'var(--r-sm)',
            padding: '4px 8px',
            fontSize: '12px',
          }}
        >
          {devices.length === 0 && <option value="">No devices detected</option>}
          {devices.map((d) => (
            <option key={d.name} value={d.name}>
              {d.name} {d.isOpen ? '(connected)' : ''}
            </option>
          ))}
        </select>
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: '50%',
            background: devices.find((d) => d.name === selectedDevice)?.isOpen ? '#4a9' : '#666',
          }}
        />
      </div>

      {/* Learning overlay */}
      {learning && (
        <div
          style={{
            background: 'var(--bg-inset)',
            border: '1px solid var(--accent)',
            borderRadius: 'var(--r-sm)',
            padding: '12px',
            marginBottom: '12px',
            textAlign: 'center',
          }}
        >
          <div style={{ marginBottom: '8px', color: 'var(--accent)' }}>
            Learning: <strong>{learning}</strong>
          </div>
          <div style={{ fontSize: '12px', color: 'var(--fg-2)', marginBottom: '8px' }}>
            {lastCapture
              ? `Captured: ${CONTROL_TYPES[lastCapture.controlType]} Ch${lastCapture.channel} #${lastCapture.controlId} = ${lastCapture.value}`
              : 'Move a control on your MIDI device...'}
          </div>
          <div style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
            <button
              onClick={() => stopLearn(true)}
              disabled={!lastCapture}
              style={{
                background: lastCapture ? 'var(--accent)' : 'var(--bg-1)',
                color: lastCapture ? '#fff' : 'var(--fg-3)',
                border: 'none',
                borderRadius: 'var(--r-sm)',
                padding: '4px 12px',
                cursor: lastCapture ? 'pointer' : 'default',
                fontSize: '12px',
              }}
            >
              Save
            </button>
            <button
              onClick={() => stopLearn(false)}
              style={{
                background: 'var(--bg-1)',
                color: 'var(--fg-0)',
                border: '1px solid var(--border-0)',
                borderRadius: 'var(--r-sm)',
                padding: '4px 12px',
                cursor: 'pointer',
                fontSize: '12px',
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Mapping table */}
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12px' }}>
        <thead>
          <tr style={{ borderBottom: '1px solid var(--border-0)', color: 'var(--fg-2)' }}>
            <th style={{ textAlign: 'left', padding: '4px 6px' }}>Control</th>
            <th style={{ textAlign: 'left', padding: '4px 6px' }}>Type</th>
            <th style={{ textAlign: 'left', padding: '4px 6px' }}>Command</th>
            <th style={{ textAlign: 'right', padding: '4px 6px' }}></th>
          </tr>
        </thead>
        <tbody>
          {deviceMappings.length === 0 && (
            <tr>
              <td colSpan={4} style={{ padding: '12px', textAlign: 'center', color: 'var(--fg-3)' }}>
                No mappings configured. Add one below.
              </td>
            </tr>
          )}
          {deviceMappings.map((m, i) => (
            <tr key={i} style={{ borderBottom: '1px solid var(--border-0)' }}>
              <td style={{ padding: '4px 6px' }}>
                {CONTROL_TYPES[m.controlType] ?? '?'} {m.controlId} Ch{m.channel}
              </td>
              <td style={{ padding: '4px 6px' }}>{m.relative ? 'Wheel' : m.controlType === 1 ? 'Button' : 'Knob'}</td>
              <td style={{ padding: '4px 6px' }}>{commands[m.command] ?? `#${m.command}`}</td>
              <td style={{ padding: '4px 6px', textAlign: 'right' }}>
                <button
                  onClick={() => commands[m.command] && startLearn(commands[m.command])}
                  disabled={!!learning}
                  style={{ background: 'none', border: 'none', color: 'var(--accent)', cursor: 'pointer', fontSize: '12px', marginRight: '6px' }}
                >
                  Learn
                </button>
                <button
                  onClick={() => deleteMapping(i)}
                  style={{ background: 'none', border: 'none', color: 'var(--tx)', cursor: 'pointer', fontSize: '12px' }}
                >
                  ×
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Add mapping */}
      <div style={{ display: 'flex', gap: '8px', marginTop: '10px', alignItems: 'center' }}>
        <select
          value={addCommand}
          onChange={(e) => setAddCommand(e.target.value)}
          style={{
            flex: 1,
            background: 'var(--bg-1)',
            color: 'var(--fg-0)',
            border: '1px solid var(--border-0)',
            borderRadius: 'var(--r-sm)',
            padding: '4px 8px',
            fontSize: '12px',
          }}
        >
          <option value="">Select command...</option>
          {commands.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <button
          onClick={addMapping}
          disabled={!addCommand || !selectedDevice || !!learning}
          style={{
            background: addCommand ? 'var(--accent)' : 'var(--bg-1)',
            color: addCommand ? '#fff' : 'var(--fg-3)',
            border: 'none',
            borderRadius: 'var(--r-sm)',
            padding: '4px 12px',
            cursor: addCommand ? 'pointer' : 'default',
            fontSize: '12px',
            whiteSpace: 'nowrap',
          }}
        >
          + Learn
        </button>
      </div>
    </div>
  );
}

export default function init(api: ZeusPluginApi) {
  api.registerPanel({
    id: 'midi.settings',
    component: () => <MidiSettingsPanel api={api} />,
  });
}
