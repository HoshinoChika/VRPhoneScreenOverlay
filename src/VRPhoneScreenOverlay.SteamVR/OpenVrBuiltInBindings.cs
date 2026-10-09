using System.Collections.Frozen;
using System.Text;

namespace VRPhoneScreenOverlay.SteamVR;

// Compiled source of truth. Exported JSON is disposable input for SteamVR,
// never the only copy of defaults and never a source of user preferences.
internal static class OpenVrBuiltInBindings
{
    internal const string ManifestJson = """
        {
          "supports_dominant_hand_setting": true,
          "default_bindings": [
            { "controller_type": "vive_controller", "binding_url": "bindings/vive_controller.json" },
            { "controller_type": "holographic_controller", "binding_url": "bindings/wmr.json" },
            { "controller_type": "knuckles", "binding_url": "bindings/knuckles.json" },
            { "controller_type": "oculus_touch", "binding_url": "bindings/oculus_touch.json" },
            { "controller_type": "hpmotioncontroller", "binding_url": "bindings/hp_wmr.json" },
            { "controller_type": "pico_controller", "binding_url": "bindings/pico_controller.json" },
            { "controller_type": "pico_controller_ice", "binding_url": "bindings/pico_controller_ice.json" }
          ],
          "actions": [
            { "name": "/actions/main/in/LeftHandSpaceDrag", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/main/in/RightHandSpaceDrag", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/main/in/ResetOffsets", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/main/in/PhoneBack", "requirement": "suggested", "type": "boolean" },
            { "name": "/actions/main/in/PhoneHome", "requirement": "suggested", "type": "boolean" },
            { "name": "/actions/main/in/PhoneRecents", "requirement": "suggested", "type": "boolean" },
            { "name": "/actions/main/in/PhoneControlPanel", "requirement": "suggested", "type": "boolean" },
            { "name": "/actions/main/in/PhoneScreenshot", "requirement": "suggested", "type": "boolean" },
            { "name": "/actions/main/in/PhoneOverlayGrab", "requirement": "mandatory", "type": "boolean" },
            { "name": "/actions/main/in/PhoneOverlayScale", "requirement": "optional", "type": "vector2" },
            { "name": "/actions/main/in/PhoneOverlayTouch", "requirement": "mandatory", "type": "boolean" },
            { "name": "/actions/main/in/PhonePointerPose", "requirement": "mandatory", "type": "pose" },
            { "name": "/actions/pointer/in/PhonePointerPose", "requirement": "optional", "type": "pose" },
            { "name": "/actions/pointer/in/MenuDismiss", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/phonerecall/in/RecallMenu", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/phonebuttonstate/in/AnyPhoneInputPressed", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/phonecapture/in/button", "requirement": "optional", "type": "boolean" },
            { "name": "/actions/phonecapture/in/scalar", "requirement": "optional", "type": "vector1" },
            { "name": "/actions/phonecapture/in/axis", "requirement": "optional", "type": "vector2" }
          ],
          "action_sets": [
            { "name": "/actions/main", "usage": "leftright" },
            { "name": "/actions/pointer", "usage": "hidden" },
            { "name": "/actions/phonerecall", "usage": "hidden" },
            { "name": "/actions/phonebuttonstate", "usage": "hidden" },
            { "name": "/actions/phonecapture", "usage": "hidden" }
          ],
          "localization": [
            {
              "language_tag": "en_US",
              "/actions/main": "VRPhoneScreen Overlay",
              "/actions/main/in/LeftHandSpaceDrag": "Left Hand Space Drag",
              "/actions/main/in/RightHandSpaceDrag": "Right Hand Space Drag",
              "/actions/main/in/ResetOffsets": "Reset Space Offset",
              "/actions/main/in/PhoneBack": "Android Back",
              "/actions/main/in/PhoneHome": "Android Home",
              "/actions/main/in/PhoneRecents": "Android Recent Apps",
              "/actions/main/in/PhoneControlPanel": "Android Control Panel",
              "/actions/main/in/PhoneScreenshot": "Android Screenshot",
              "/actions/main/in/PhoneOverlayGrab": "Grab Phone Overlay",
              "/actions/main/in/PhoneOverlayScale": "Scale Phone Overlay",
              "/actions/main/in/PhoneOverlayTouch": "Touch Phone Overlay",
              "/actions/main/in/PhonePointerPose": "Phone Pointer Pose",
              "/actions/pointer": "Internal Phone Pointer",
              "/actions/pointer/in/PhonePointerPose": "Internal Phone Pointer Pose",
              "/actions/phonebuttonstate": "Internal Phone Input State",
              "/actions/phonebuttonstate/in/AnyPhoneInputPressed": "Any Phone Input Pressed"
            },
            {
              "language_tag": "zh_CN",
              "/actions/main": "\u7a7a\u95f4\u62d6\u62fd",
              "/actions/main/in/LeftHandSpaceDrag": "\u5de6\u624b\u7a7a\u95f4\u62d6\u62fd",
              "/actions/main/in/RightHandSpaceDrag": "\u53f3\u624b\u7a7a\u95f4\u62d6\u62fd",
              "/actions/main/in/ResetOffsets": "\u91cd\u7f6e\u7a7a\u95f4\u504f\u79fb",
              "/actions/main/in/PhoneBack": "\u624b\u673a\u8fd4\u56de",
              "/actions/main/in/PhoneHome": "\u624b\u673a\u56de\u5230\u684c\u9762",
              "/actions/main/in/PhoneRecents": "\u624b\u673a\u6700\u8fd1\u4efb\u52a1",
              "/actions/main/in/PhoneControlPanel": "\u624b\u673a\u63a7\u5236\u680f",
              "/actions/main/in/PhoneScreenshot": "\u624b\u673a\u622a\u5c4f",
              "/actions/main/in/PhoneOverlayGrab": "\u79fb\u52a8\u624b\u673a\u6d6e\u7a97",
              "/actions/main/in/PhoneOverlayScale": "\u7f29\u653e\u624b\u673a\u6d6e\u7a97",
              "/actions/main/in/PhoneOverlayTouch": "\u89e6\u63a7\u624b\u673a\u6d6e\u7a97",
              "/actions/main/in/PhonePointerPose": "\u624b\u673a\u6307\u9488\u59ff\u6001",
              "/actions/pointer": "\u5185\u90e8\u624b\u673a\u6307\u9488",
              "/actions/pointer/in/PhonePointerPose": "\u5185\u90e8\u624b\u673a\u6307\u9488\u59ff\u6001",
              "/actions/phonebuttonstate": "\u5185\u90e8\u624b\u673a\u8f93\u5165\u72b6\u6001",
              "/actions/phonebuttonstate/in/AnyPhoneInputPressed": "\u4efb\u610f\u624b\u673a\u8f93\u5165\u5df2\u6309\u4e0b"
            }
          ]
        }
        """;

    internal static FrozenDictionary<string, string> Bindings { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["vive_controller"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "vive_controller",
              "name": "VRPhoneScreen Overlay - Vive Wand Default",
              "description": "PICO-style layout adapted for Vive Wand controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/trackpad", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trackpad", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "double": { "output": "/actions/main/in/phonescreenshot" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": {
                    "double_press_delay": 0.3,
                    "long_press_delay": 0.65,
                    "long_press_expiry": 3
                  } },
                  { "path": "/user/hand/right/input/trackpad", "mode": "trackpad", "inputs": {
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } },
                  { "path": "/user/hand/right/input/trigger", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
        ["holographic_controller"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "holographic_controller",
              "name": "VRPhoneScreen Overlay - WMR Default",
              "description": "PICO-style layout adapted for classic Windows Mixed Reality controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/raw" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/joystick", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trackpad", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trigger", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  } },
                  { "path": "/user/hand/right/input/joystick", "mode": "joystick", "inputs": {
                    "click": { "output": "/actions/main/in/phonescreenshot" },
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
        ["knuckles"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "knuckles",
              "name": "VRPhoneScreen Overlay - Index Default",
              "description": "PICO-style layout for Valve Index controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/b", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/a", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/b", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/a", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trigger", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  }, "parameters": {
                    "click_activate_threshold": 0.3,
                    "click_deactivate_threshold": 0.25,
                    "force_input": "force",
                    "haptic_amplitude": 0.6
                  } },
                  { "path": "/user/hand/right/input/thumbstick", "mode": "joystick", "inputs": {
                    "click": { "output": "/actions/main/in/phonescreenshot" },
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
        ["oculus_touch"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "oculus_touch",
              "name": "VRPhoneScreen Overlay - Touch Default",
              "description": "PICO-style layout for Oculus and Meta Touch controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/y", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/x", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/b", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/a", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trigger", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  } },
                  { "path": "/user/hand/right/input/joystick", "mode": "joystick", "inputs": {
                    "click": { "output": "/actions/main/in/phonescreenshot" },
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
        ["hpmotioncontroller"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "hpmotioncontroller",
              "name": "VRPhoneScreen Overlay - HP Reverb G2 Default",
              "description": "PICO-style layout for HP Windows Mixed Reality controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/raw" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/y", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/x", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/b", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/a", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trigger", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  } },
                  { "path": "/user/hand/right/input/joystick", "mode": "joystick", "inputs": {
                    "click": { "output": "/actions/main/in/phonescreenshot" },
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
        ["pico_controller"] = """
            {
              "action_manifest_version": 0,
              "alias_info": {},
              "app_key": "local.spacedraglite.desktop.v1",
              "bindings": {
                "/actions/main": {
                  "poses": [
                    {
                      "output": "/actions/main/in/phonepointerpose",
                      "path": "/user/hand/right/pose/tip"
                    }
                  ],
                  "skeleton": [],
                  "sources": [
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/lefthandspacedrag"
                        }
                      },
                      "mode": "button",
                      "parameters": {},
                      "path": "/user/hand/left/input/y"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/phoneback"
                        },
                        "long": {
                          "output": "/actions/main/in/phonehome"
                        }
                      },
                      "mode": "button",
                      "parameters": {
                        "long_press_delay": 0.65,
                        "long_press_expiry": 3
                      },
                      "path": "/user/hand/right/input/b"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/phonerecents"
                        },
                        "long": {
                          "output": "/actions/main/in/phonecontrolpanel"
                        }
                      },
                      "mode": "button",
                      "parameters": {
                        "long_press_delay": 0.65,
                        "long_press_expiry": 3
                      },
                      "path": "/user/hand/right/input/a"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/phoneoverlaytouch"
                        }
                      },
                      "mode": "trigger",
                      "parameters": {},
                      "path": "/user/hand/right/input/trigger"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/phoneoverlaygrab"
                        }
                      },
                      "mode": "trigger",
                      "parameters": {},
                      "path": "/user/hand/right/input/grip"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/phonescreenshot"
                        },
                        "position": {
                          "output": "/actions/main/in/phoneoverlayscale"
                        }
                      },
                      "mode": "joystick",
                      "parameters": {},
                      "path": "/user/hand/right/input/joystick"
                    },
                    {
                      "inputs": {
                        "click": {
                          "output": "/actions/main/in/resetoffsets"
                        }
                      },
                      "mode": "button",
                      "parameters": {},
                      "path": "/user/hand/left/input/x"
                    }
                  ]
                }
              },
              "category": "steamvr_input",
              "controller_type": "pico_controller",
              "description": "Saved PICO binding: Y drag, X reset, B back/home, A recents/control panel, trigger touch, grip grab, joystick scale/screenshot",
              "interaction_profile": "",
              "name": "VRPhoneScreen Overlay - Saved PICO Default",
              "options": {},
              "simulated_actions": []
            }
            """,
        ["pico_controller_ice"] = """
            {
              "action_manifest_version": 0,
              "app_key": "local.spacedraglite.desktop.v1",
              "controller_type": "pico_controller_ice",
              "name": "VRPhoneScreen Overlay - PICO Trackpad Default",
              "description": "PICO-style layout adapted for legacy PICO trackpad controllers",
              "bindings": { "/actions/main": {
                "poses": [
                  { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                ],
                "sources": [
                  { "path": "/user/hand/left/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/lefthandspacedrag" }
                  } },
                  { "path": "/user/hand/left/input/trackpad", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/resetoffsets" }
                  } },
                  { "path": "/user/hand/right/input/application_menu", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                  }, "parameters": { "long_press_delay": 0.65, "long_press_expiry": 3 } },
                  { "path": "/user/hand/right/input/trackpad", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phonerecents" },
                    "double": { "output": "/actions/main/in/phonescreenshot" },
                    "long": { "output": "/actions/main/in/phonecontrolpanel" }
                  }, "parameters": {
                    "double_press_delay": 0.3,
                    "long_press_delay": 0.65,
                    "long_press_expiry": 3
                  } },
                  { "path": "/user/hand/right/input/trackpad", "mode": "trackpad", "inputs": {
                    "position": { "output": "/actions/main/in/phoneoverlayscale" }
                  } },
                  { "path": "/user/hand/right/input/trigger", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                  } },
                  { "path": "/user/hand/right/input/grip", "mode": "trigger", "inputs": {
                    "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                  } }
                ]
              } },
              "options": {},
              "simulated_actions": []
            }
            """,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static string SourceDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRPhoneScreenOverlay", "steamvr", "default-source");

    internal static string EnsureSourceFiles(string directory)
    {
        using System.Text.Json.JsonDocument manifest = System.Text.Json.JsonDocument.Parse(ManifestJson);
        foreach (System.Text.Json.JsonElement entry in manifest.RootElement.GetProperty("default_bindings").EnumerateArray())
        {
            string type = entry.GetProperty("controller_type").GetString()!;
            WriteIfChanged(Path.Combine(directory, entry.GetProperty("binding_url").GetString()!), Bindings[type]);
        }
        string path = Path.Combine(directory, "action_manifest.json");
        WriteIfChanged(path, ManifestJson);
        return path;
    }

    private static void WriteIfChanged(string path, string json)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidDataException("Default export path contains a reparse point."); }
        }
        if (File.Exists(path) && new FileInfo(path).Length <= 1_048_576 && File.ReadAllText(path) == json) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".new";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
