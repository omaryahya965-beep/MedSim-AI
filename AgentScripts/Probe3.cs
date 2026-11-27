using UnityEngine; using System.Linq; using MedSimAI.Instruments;
var z = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
return string.Join("\n", z.Select(p => p.zoneId + " hl=" + (p.highlight ? p.highlight.enabled.ToString() : "null") + " pos=" + p.transform.position));
