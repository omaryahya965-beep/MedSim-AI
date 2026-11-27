using System.Linq; using UnityEngine; using MedSimAI.Instruments; using MedSimAI.Voice; using MedSimAI.AI;
var st = Object.FindAnyObjectByType<StethoscopeController>();
var z = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(p => p.zoneId == "CHEST_MITRAL");
st.transform.rotation = Quaternion.FromToRotation(Vector3.down, -z.transform.forward);
st.transform.position += z.transform.position - st.probePoint.position;
z.SetHighlight(true, true);
_ = VoiceInteractionManager.Instance.Submit(Persona.Patient, "Does the pain spread anywhere?");
return "ok";
