namespace AutoTrigger.Tests
{
    internal static class SettingsFixtures
    {
        public const string Version120 = @"{
  ""schemaVersion"": 1,
  ""startMinimized"": true,
  ""closeToTray"": true,
  ""theme"": ""dark"",
  ""focusStabilityMs"": 450,
  ""startPollMs"": 750,
  ""logLevel"": ""detailed"",
  ""paused"": true,
  ""window"": {
    ""width"": 1180,
    ""height"": 760
  },
  ""applications"": [
    {
      ""id"": ""55555555-5555-5555-5555-555555555555"",
      ""name"": ""CONTROL Resonant"",
      ""path"": ""D:\\SteamLibrary\\steamapps\\common\\CONTROL Resonant\\CONTROLResonant.exe"",
      ""matchBy"": ""path"",
      ""profileId"": ""44444444-4444-4444-4444-444444444444"",
      ""enabled"": true
    },
    {
      ""id"": ""66666666-6666-6666-6666-666666666666"",
      ""name"": ""Game Pass game"",
      ""path"": ""C:\\XboxGames\\Some Game\\Content\\Game.exe"",
      ""matchBy"": ""fileName"",
      ""profileId"": ""44444444-4444-4444-4444-444444444444"",
      ""enabled"": false
    },
    {
      ""id"": ""77777777-7777-7777-7777-777777777777"",
      ""name"": """",
      ""path"": ""C:\\Program Files\\Tool\\tool.exe"",
      ""matchBy"": ""path"",
      ""profileId"": null,
      ""enabled"": true
    }
  ],
  ""profiles"": [
    {
      ""id"": ""44444444-4444-4444-4444-444444444444"",
      ""name"": ""VRR"",
      ""restartApp"": true,
      ""shortcut"": {
        ""keys"": ""Ctrl+Alt+1"",
        ""ctrl"": true,
        ""alt"": true,
        ""shift"": false,
        ""key"": 49
      },
      ""closedShortcut"": {
        ""keys"": ""Ctrl+Shift+F1"",
        ""ctrl"": true,
        ""alt"": false,
        ""shift"": true,
        ""key"": 112
      },
      ""onStart"": [
        {
          ""type"": ""display"",
          ""delayMs"": 0,
          ""displayId"": ""11111111-1111-1111-1111-111111111111"",
          ""hdr"": ""on"",
          ""resolution"": {
            ""width"": 3840,
            ""height"": 2160
          },
          ""refresh"": {
            ""numerator"": 120000,
            ""denominator"": 1000
          },
          ""colorDepthBpc"": 10,
          ""colorFormat"": ""rgb"",
          ""dynamicRange"": ""full""
        },
        {
          ""type"": ""display"",
          ""delayMs"": 1500,
          ""displayId"": null,
          ""hdr"": ""unchanged"",
          ""resolution"": null,
          ""refresh"": ""max"",
          ""colorDepthBpc"": 0,
          ""colorFormat"": """",
          ""dynamicRange"": """"
        },
        {
          ""type"": ""includeProfile"",
          ""delayMs"": 200,
          ""profileId"": ""33333333-3333-3333-3333-333333333333""
        },
        {
          ""type"": ""windowsSettings"",
          ""delayMs"": 0,
          ""playbackDevice"": """",
          ""recordDevice"": """",
          ""playbackName"": """",
          ""recordName"": """",
          ""planId"": ""8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"",
          ""planName"": ""High performance"",
          ""screenSaver"": ""off""
        },
        {
          ""type"": ""runProgram"",
          ""delayMs"": 300,
          ""path"": ""C:\\Tools\\Voicemeeter\\VoicemeeterOpen.exe"",
          ""arguments"": ""-profile \""game\"" -silent"",
          ""holdNext"": true,
          ""holdCapSec"": 45
        },
        {
          ""type"": ""sendKeys"",
          ""delayMs"": 100,
          ""keys"": ""Ctrl+Alt+1"",
          ""ctrl"": true,
          ""alt"": true,
          ""shift"": false,
          ""key"": 49
        }
      ],
      ""onClose"": [
        {
          ""type"": ""closeProgram"",
          ""delayMs"": 0,
          ""target"": ""VoicemeeterOpen.exe"",
          ""force"": true,
          ""forceAfterSec"": 8
        },
        {
          ""type"": ""sendKeys"",
          ""delayMs"": 0,
          ""keys"": ""Ctrl+Alt+0"",
          ""ctrl"": true,
          ""alt"": true,
          ""shift"": false,
          ""key"": 48
        },
        {
          ""type"": ""display"",
          ""delayMs"": 5000,
          ""displayId"": ""11111111-1111-1111-1111-111111111111"",
          ""hdr"": ""off"",
          ""resolution"": null,
          ""refresh"": null,
          ""colorDepthBpc"": 0,
          ""colorFormat"": """",
          ""dynamicRange"": """"
        },
        {
          ""type"": ""windowsSettings"",
          ""delayMs"": 0,
          ""playbackDevice"": """",
          ""recordDevice"": """",
          ""playbackName"": """",
          ""recordName"": """",
          ""planId"": null,
          ""planName"": """",
          ""screenSaver"": ""on""
        }
      ],
      ""onFocus"": [
        {
          ""type"": ""runProgram"",
          ""delayMs"": 0,
          ""path"": ""notepad.exe"",
          ""arguments"": """",
          ""holdNext"": false,
          ""holdCapSec"": 30
        }
      ],
      ""onFocusLost"": [
        {
          ""type"": ""closeProgram"",
          ""delayMs"": 0,
          ""target"": ""C:\\Tools\\overlay.exe"",
          ""force"": false,
          ""forceAfterSec"": 5
        }
      ]
    },
    {
      ""id"": ""33333333-3333-3333-3333-333333333333"",
      ""name"": ""Audio TV"",
      ""restartApp"": false,
      ""shortcut"": null,
      ""closedShortcut"": null,
      ""onStart"": [
        {
          ""type"": ""windowsSettings"",
          ""delayMs"": 0,
          ""playbackDevice"": ""{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000001}"",
          ""recordDevice"": ""{0.0.1.00000000}.{aaaaaaaa-0000-0000-0000-000000000002}"",
          ""playbackName"": ""TV (NVIDIA High Definition Audio)"",
          ""recordName"": ""Microphone (USB)"",
          ""planId"": null,
          ""planName"": """",
          ""screenSaver"": ""unchanged""
        }
      ],
      ""onClose"": [],
      ""onFocus"": [],
      ""onFocusLost"": []
    }
  ],
  ""displays"": [
    {
      ""id"": ""11111111-1111-1111-1111-111111111111"",
      ""legacyUid"": 724359,
      ""devicePath"": ""\\\\?\\DISPLAY#SAM7179#5&1a2b3c4d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"",
      ""friendlyName"": ""Samsung Q90A"",
      ""managed"": true,
      ""known"": {
        ""desktopSizes"": [
          [
            3840,
            2160
          ],
          [
            2560,
            1440
          ]
        ],
        ""signal"": [
          3840,
          2160
        ],
        ""refreshRates"": [
          [
            120000,
            1000
          ],
          [
            59940,
            1000
          ]
        ],
        ""hdr"": true,
        ""nvidia"": true,
        ""colors"": [
          [
            10,
            ""rgb"",
            ""full""
          ],
          [
            12,
            ""ycbcr422"",
            ""limited""
          ]
        ]
      }
    },
    {
      ""id"": ""22222222-2222-2222-2222-222222222222"",
      ""legacyUid"": -1,
      ""devicePath"": ""\\\\?\\DISPLAY#DEL41A8#5&1a2b3c4d&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"",
      ""friendlyName"": ""DELL U2720Q"",
      ""managed"": false,
      ""known"": null
    }
  ]
}";
    }
}
