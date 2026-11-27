using UnityEngine; using System.Linq;
var cam = Camera.main;
var all = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Select(c => c.name + "@" + c.transform.position + " en=" + c.enabled + " depth=" + c.depth + " tgt=" + (c.targetTexture!=null));
var hits = Physics.OverlapSphere(cam.transform.position, 0.1f, ~0, QueryTriggerInteraction.Collide).Select(h => h.name);
return "main=" + cam.name + " pos=" + cam.transform.position + " fwd=" + cam.transform.forward + " | cams: " + string.Join("; ", all) + " | near: " + string.Join(",", hits) + " | xr=" + UnityEngine.XR.XRSettings.isDeviceActive;
