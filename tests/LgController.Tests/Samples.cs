using System.Text.Json;

namespace LgController.Tests;

public static class Samples
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public const string Profile = """
        {
          "notification": { "push": ["WATER_IS_FULL"] },
          "property": {
            "operation": {
              "airConOperationMode": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["POWER_ON", "POWER_OFF"], "w": ["POWER_ON", "POWER_OFF"] } }
            },
            "airConJobMode": {
              "currentJobMode": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["COOL", "AIR_DRY", "FAN", "AUTO"], "w": ["COOL", "AIR_DRY", "FAN", "AUTO"] } }
            },
            "temperature": {
              "currentTemperature": { "type": "number", "mode": ["r"] },
              "targetTemperature": { "type": "range", "mode": ["r", "w"],
                "value": { "r": { "max": 30, "min": 18, "step": 1 }, "w": { "max": 30, "min": 18, "step": 1 } } },
              "unit": { "type": "enum", "mode": ["r"], "value": { "r": ["C", "F"] } }
            },
            "temperatureInUnits": [
              { "unit": "F",
                "currentTemperature": { "type": "number", "mode": ["r"] },
                "targetTemperature": { "type": "range", "mode": ["r", "w"],
                  "value": { "r": { "max": 86, "min": 64, "step": 1 }, "w": { "max": 86, "min": 64, "step": 1 } } } },
              { "unit": "C",
                "currentTemperature": { "type": "number", "mode": ["r"] },
                "targetTemperature": { "type": "range", "mode": ["r", "w"],
                  "value": { "r": { "max": 30, "min": 16, "step": 0.5 }, "w": { "max": 30, "min": 16, "step": 0.5 } } } }
            ],
            "airFlow": {
              "windStrength": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["LOW", "MID", "HIGH", "AUTO"], "w": ["LOW", "MID", "HIGH", "AUTO"] } }
            },
            "windDirection": {
              "rotateUpDown": { "type": "boolean", "mode": ["r", "w"],
                "value": { "r": [true, false], "w": [true, false] } }
            },
            "powerSave": {
              "powerSaveEnabled": { "type": "boolean", "mode": ["r", "w"],
                "value": { "r": [true, false], "w": [true, false] } }
            },
            "timer": {
              "relativeHourToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeMinuteToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeStopTimer": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["SET", "UNSET"], "w": ["UNSET"] } },
              "relativeHourToStart": { "type": "number", "mode": ["r", "w"] },
              "relativeMinuteToStart": { "type": "number", "mode": ["r", "w"] },
              "relativeStartTimer": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["SET", "UNSET"], "w": ["UNSET"] } }
            },
            "sleepTimer": {
              "relativeHourToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeMinuteToStop": { "type": "number", "mode": ["r", "w"] },
              "relativeStopTimer": { "type": "enum", "mode": ["r", "w"],
                "value": { "r": ["SET", "UNSET"], "w": ["UNSET"] } }
            },
            "filterInfo": {
              "filterLifetime": { "type": "number", "mode": ["r"] },
              "usedTime": { "type": "number", "mode": ["r"] }
            }
          }
        }
        """;

    public const string State = """
        {
          "operation": { "airConOperationMode": "POWER_ON" },
          "airConJobMode": { "currentJobMode": "COOL" },
          "temperature": { "currentTemperature": 26, "targetTemperature": 23, "unit": "C" },
          "temperatureInUnits": [
            { "currentTemperature": 79, "targetTemperature": 73, "unit": "F" },
            { "currentTemperature": 26.5, "targetTemperature": 23, "unit": "C" }
          ],
          "airFlow": { "windStrength": "MID" },
          "windDirection": { "rotateUpDown": true, "rotateLeftRight": false },
          "powerSave": { "powerSaveEnabled": false },
          "timer": { "relativeHourToStop": 1, "relativeMinuteToStop": 30, "relativeStopTimer": "SET",
            "relativeStartTimer": "UNSET" },
          "sleepTimer": { "relativeHourToStop": 2, "relativeMinuteToStop": 0, "relativeStopTimer": "SET" },
          "filterInfo": { "filterLifetime": 0, "usedTime": 720 }
        }
        """;
}
