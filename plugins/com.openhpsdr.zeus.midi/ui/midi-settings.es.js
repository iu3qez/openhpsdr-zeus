// Zeus.Plugins.Midi/ui/midi-settings.tsx
import { useState, useEffect, useCallback, useRef } from "react";
import { jsx, jsxs } from "react/jsx-runtime";
var CONTROL_TYPES = ["CC", "NoteOn", "NoteOff", "PitchBend"];
function MidiSettingsPanel({ api }) {
  const [devices, setDevices] = useState([]);
  const [selectedDevice, setSelectedDevice] = useState("");
  const [mappings, setMappings] = useState({ version: 1, devices: {} });
  const [commands, setCommands] = useState([]);
  const [learning, setLearning] = useState(null);
  const [lastCapture, setLastCapture] = useState(null);
  const [addCommand, setAddCommand] = useState("");
  const pollRef = useRef(null);
  const fetchDevices = useCallback(async () => {
    try {
      const r = await api.callBackend("GET", "/devices");
      if (r.ok) setDevices(await r.json());
    } catch {
    }
  }, [api]);
  const fetchMappings = useCallback(async () => {
    try {
      const r = await api.callBackend("GET", "/mappings");
      if (r.ok) setMappings(await r.json());
    } catch {
    }
  }, [api]);
  const fetchCommands = useCallback(async () => {
    try {
      const r = await api.callBackend("GET", "/commands");
      if (r.ok) setCommands(await r.json());
    } catch {
    }
  }, [api]);
  useEffect(() => {
    fetchDevices();
    fetchMappings();
    fetchCommands();
    const iv = setInterval(fetchDevices, 3e3);
    return () => clearInterval(iv);
  }, [fetchDevices, fetchMappings, fetchCommands]);
  useEffect(() => {
    if (devices.length > 0 && !selectedDevice) {
      setSelectedDevice(devices[0].name);
    }
  }, [devices, selectedDevice]);
  const saveMappings = async (updated) => {
    setMappings(updated);
    await api.callBackend("PUT", "/mappings", updated);
  };
  const deleteMapping = async (idx) => {
    if (!selectedDevice) return;
    const dev = mappings.devices[selectedDevice];
    if (!dev) return;
    const updated = {
      ...mappings,
      devices: {
        ...mappings.devices,
        [selectedDevice]: {
          mappings: dev.mappings.filter((_, i) => i !== idx)
        }
      }
    };
    await saveMappings(updated);
  };
  const startLearn = async (commandName) => {
    if (!selectedDevice) return;
    setLearning(commandName);
    setLastCapture(null);
    const cmdIdx = commands.indexOf(commandName);
    await api.callBackend("POST", "/learn/start", { deviceName: selectedDevice, command: cmdIdx });
    pollRef.current = setInterval(async () => {
      try {
        const r = await api.callBackend("GET", "/learn/last");
        if (r.ok) {
          const data = await r.json();
          if (data) setLastCapture(data);
        }
      } catch {
      }
    }, 200);
  };
  const stopLearn = async (save) => {
    if (pollRef.current) {
      clearInterval(pollRef.current);
      pollRef.current = null;
    }
    const r = await api.callBackend("POST", "/learn/stop");
    const result = r.ok ? await r.json() : null;
    if (save && result && learning && selectedDevice) {
      const existing = mappings.devices[selectedDevice]?.mappings ?? [];
      const filtered = existing.filter(
        (m) => !(m.controlType === result.controlType && m.channel === result.channel && m.controlId === result.controlId)
      );
      const cmdIdx = commands.indexOf(learning);
      const newMapping = {
        controlId: result.controlId,
        controlType: result.controlType,
        channel: result.channel,
        command: cmdIdx,
        toggle: false,
        relative: false,
        encoderMode: 0,
        stepMultiplier: 1
      };
      const updated = {
        ...mappings,
        devices: {
          ...mappings.devices,
          [selectedDevice]: { mappings: [...filtered, newMapping] }
        }
      };
      await saveMappings(updated);
    }
    setLearning(null);
    setLastCapture(null);
  };
  const addMapping = () => {
    if (!addCommand || !selectedDevice) return;
    startLearn(addCommand);
    setAddCommand("");
  };
  const deviceMappings = selectedDevice ? mappings.devices[selectedDevice]?.mappings ?? [] : [];
  return /* @__PURE__ */ jsxs("div", { style: { padding: "12px", fontFamily: "var(--ff-ui, sans-serif)", color: "var(--fg-0)", fontSize: "13px" }, children: [
    /* @__PURE__ */ jsx("h3", { style: { margin: "0 0 12px", fontSize: "15px", fontWeight: 600 }, children: "MIDI Controllers" }),
    /* @__PURE__ */ jsxs("div", { style: { display: "flex", alignItems: "center", gap: "8px", marginBottom: "12px" }, children: [
      /* @__PURE__ */ jsx("label", { style: { color: "var(--fg-2)", fontSize: "12px" }, children: "Device:" }),
      /* @__PURE__ */ jsxs(
        "select",
        {
          value: selectedDevice,
          onChange: (e) => setSelectedDevice(e.target.value),
          style: {
            flex: 1,
            background: "var(--bg-1)",
            color: "var(--fg-0)",
            border: "1px solid var(--border-0)",
            borderRadius: "var(--r-sm)",
            padding: "4px 8px",
            fontSize: "12px"
          },
          children: [
            devices.length === 0 && /* @__PURE__ */ jsx("option", { value: "", children: "No devices detected" }),
            devices.map((d) => /* @__PURE__ */ jsxs("option", { value: d.name, children: [
              d.name,
              " ",
              d.isOpen ? "(connected)" : ""
            ] }, d.name))
          ]
        }
      ),
      /* @__PURE__ */ jsx(
        "span",
        {
          style: {
            width: 8,
            height: 8,
            borderRadius: "50%",
            background: devices.find((d) => d.name === selectedDevice)?.isOpen ? "#4a9" : "#666"
          }
        }
      )
    ] }),
    learning && /* @__PURE__ */ jsxs(
      "div",
      {
        style: {
          background: "var(--bg-inset)",
          border: "1px solid var(--accent)",
          borderRadius: "var(--r-sm)",
          padding: "12px",
          marginBottom: "12px",
          textAlign: "center"
        },
        children: [
          /* @__PURE__ */ jsxs("div", { style: { marginBottom: "8px", color: "var(--accent)" }, children: [
            "Learning: ",
            /* @__PURE__ */ jsx("strong", { children: learning })
          ] }),
          /* @__PURE__ */ jsx("div", { style: { fontSize: "12px", color: "var(--fg-2)", marginBottom: "8px" }, children: lastCapture ? `Captured: ${CONTROL_TYPES[lastCapture.controlType]} Ch${lastCapture.channel} #${lastCapture.controlId} = ${lastCapture.value}` : "Move a control on your MIDI device..." }),
          /* @__PURE__ */ jsxs("div", { style: { display: "flex", gap: "8px", justifyContent: "center" }, children: [
            /* @__PURE__ */ jsx(
              "button",
              {
                onClick: () => stopLearn(true),
                disabled: !lastCapture,
                style: {
                  background: lastCapture ? "var(--accent)" : "var(--bg-1)",
                  color: lastCapture ? "#fff" : "var(--fg-3)",
                  border: "none",
                  borderRadius: "var(--r-sm)",
                  padding: "4px 12px",
                  cursor: lastCapture ? "pointer" : "default",
                  fontSize: "12px"
                },
                children: "Save"
              }
            ),
            /* @__PURE__ */ jsx(
              "button",
              {
                onClick: () => stopLearn(false),
                style: {
                  background: "var(--bg-1)",
                  color: "var(--fg-0)",
                  border: "1px solid var(--border-0)",
                  borderRadius: "var(--r-sm)",
                  padding: "4px 12px",
                  cursor: "pointer",
                  fontSize: "12px"
                },
                children: "Cancel"
              }
            )
          ] })
        ]
      }
    ),
    /* @__PURE__ */ jsxs("table", { style: { width: "100%", borderCollapse: "collapse", fontSize: "12px" }, children: [
      /* @__PURE__ */ jsx("thead", { children: /* @__PURE__ */ jsxs("tr", { style: { borderBottom: "1px solid var(--border-0)", color: "var(--fg-2)" }, children: [
        /* @__PURE__ */ jsx("th", { style: { textAlign: "left", padding: "4px 6px" }, children: "Control" }),
        /* @__PURE__ */ jsx("th", { style: { textAlign: "left", padding: "4px 6px" }, children: "Type" }),
        /* @__PURE__ */ jsx("th", { style: { textAlign: "left", padding: "4px 6px" }, children: "Command" }),
        /* @__PURE__ */ jsx("th", { style: { textAlign: "right", padding: "4px 6px" } })
      ] }) }),
      /* @__PURE__ */ jsxs("tbody", { children: [
        deviceMappings.length === 0 && /* @__PURE__ */ jsx("tr", { children: /* @__PURE__ */ jsx("td", { colSpan: 4, style: { padding: "12px", textAlign: "center", color: "var(--fg-3)" }, children: "No mappings configured. Add one below." }) }),
        deviceMappings.map((m, i) => /* @__PURE__ */ jsxs("tr", { style: { borderBottom: "1px solid var(--border-0)" }, children: [
          /* @__PURE__ */ jsxs("td", { style: { padding: "4px 6px" }, children: [
            CONTROL_TYPES[m.controlType] ?? "?",
            " ",
            m.controlId,
            " Ch",
            m.channel
          ] }),
          /* @__PURE__ */ jsx("td", { style: { padding: "4px 6px" }, children: m.relative ? "Wheel" : m.controlType === 1 ? "Button" : "Knob" }),
          /* @__PURE__ */ jsx("td", { style: { padding: "4px 6px" }, children: commands[m.command] ?? `#${m.command}` }),
          /* @__PURE__ */ jsxs("td", { style: { padding: "4px 6px", textAlign: "right" }, children: [
            /* @__PURE__ */ jsx(
              "button",
              {
                onClick: () => commands[m.command] && startLearn(commands[m.command]),
                disabled: !!learning,
                style: { background: "none", border: "none", color: "var(--accent)", cursor: "pointer", fontSize: "12px", marginRight: "6px" },
                children: "Learn"
              }
            ),
            /* @__PURE__ */ jsx(
              "button",
              {
                onClick: () => deleteMapping(i),
                style: { background: "none", border: "none", color: "var(--tx)", cursor: "pointer", fontSize: "12px" },
                children: "\xD7"
              }
            )
          ] })
        ] }, i))
      ] })
    ] }),
    /* @__PURE__ */ jsxs("div", { style: { display: "flex", gap: "8px", marginTop: "10px", alignItems: "center" }, children: [
      /* @__PURE__ */ jsxs(
        "select",
        {
          value: addCommand,
          onChange: (e) => setAddCommand(e.target.value),
          style: {
            flex: 1,
            background: "var(--bg-1)",
            color: "var(--fg-0)",
            border: "1px solid var(--border-0)",
            borderRadius: "var(--r-sm)",
            padding: "4px 8px",
            fontSize: "12px"
          },
          children: [
            /* @__PURE__ */ jsx("option", { value: "", children: "Select command..." }),
            commands.map((c) => /* @__PURE__ */ jsx("option", { value: c, children: c }, c))
          ]
        }
      ),
      /* @__PURE__ */ jsx(
        "button",
        {
          onClick: addMapping,
          disabled: !addCommand || !selectedDevice || !!learning,
          style: {
            background: addCommand ? "var(--accent)" : "var(--bg-1)",
            color: addCommand ? "#fff" : "var(--fg-3)",
            border: "none",
            borderRadius: "var(--r-sm)",
            padding: "4px 12px",
            cursor: addCommand ? "pointer" : "default",
            fontSize: "12px",
            whiteSpace: "nowrap"
          },
          children: "+ Learn"
        }
      )
    ] })
  ] });
}
function init(api) {
  api.registerPanel({
    id: "midi.settings",
    component: () => /* @__PURE__ */ jsx(MidiSettingsPanel, { api })
  });
}
export {
  init as default
};
