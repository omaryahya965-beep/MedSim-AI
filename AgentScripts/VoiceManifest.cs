using MedSimAI.AI; using MedSimAI.Clinical; using System.Linq; using System.IO;
var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
var lines = ScriptedLines.Distinct(s).Select(x => x.persona.ToString().ToLowerInvariant() + "\t" + PrerenderedVoiceLibrary.KeyFor(x.text) + "\t" + x.text.Replace("\t"," ").Replace("\n"," "));
File.WriteAllLines("AgentScripts/voice_manifest.tsv", lines, new System.Text.UTF8Encoding(false));
return lines.Count();
