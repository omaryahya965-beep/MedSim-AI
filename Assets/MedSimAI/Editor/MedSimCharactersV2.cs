using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedSimAI.Characters;
using MedSimAI.Instruments;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Characters v0.2: Microsoft Rocketbox based Dr. Hassan and Mr. Reyes (prepared in Blender, see
    /// AssetStaging/CharacterPrep/README.md). Idempotent: configures the importers, builds materials and Animator
    /// Controllers, swaps the visuals under the existing scene roots (all behaviour components stay), re-fits the
    /// examination zones, the pillow and the blanket to the reclined pose.
    /// </summary>
    public static class MedSimCharactersV2
    {
        public const string Dir = "Assets/MedSimAI/Characters/v2";
        public const string DoctorFbx = Dir + "/Doctor/DrSaraHassan.fbx", PatientFbx = Dir + "/Patient/AdamReyes.fbx", BlanketFbx = Dir + "/Patient/Patient_Blanket.fbx";
        public const string DoctorCtrl = Dir + "/Animations/DrHassan.controller", PatientCtrl = Dir + "/Animations/AdamReyes.controller";
        const string MatDir = Dir + "/Materials";
        public const string PatientRoot = "Patient - Mr Adam Reyes", DoctorRoot = "Instructor - Dr Sara Hassan";
        // Scene bed (v0.2): mattress top y, backrest surface hinge line z (world), bed centre x. Patient pose was authored for 35°.
        public const float MattressTopY = 0.675f, BackrestHingeZ = 1.056f, BedCentreX = 0.30f;
        // From AssetStaging/CharacterPrep (build_patient_pose.py -- 35): hinge line is 0.052 m toward the feet from the root.
        public const float RootToHinge = 0.052f;

        [MenuItem("MedSim AI/Characters v2/1. Configure imports + materials")]
        public static string ConfigureImports()
        {
            var log = new System.Text.StringBuilder();
            Directory.CreateDirectory(MatDir);
            // ---------- textures ----------
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/Doctor/Textures", Dir + "/Patient/Textures" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p); bool ch = false;
                bool normal = p.Contains("_Normal"), spec = p.Contains("_Specular");
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want) { ti.textureType = want; ch = true; }
                if (ti.sRGBTexture == (normal || spec)) { ti.sRGBTexture = !(normal || spec); ch = true; }
                if (ti.anisoLevel != 4) { ti.anisoLevel = 4; ch = true; }
                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; ch = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ch = true; }
                bool alpha = p.Contains("HairLashes");
                if (ti.alphaIsTransparency != alpha) { ti.alphaIsTransparency = alpha; ch = true; }
                if (p.Contains("Gown_Print") && ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; ch = true; }
                if (ch) ti.SaveAndReimport();
            }
            // ---------- materials ----------
            Texture2D T(string rel) => AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/" + rel + ".png");
            Material M(string name, Texture2D col, Texture2D nrm, float smooth, Color? tint = null, bool cutout = false, bool twoSided = false)
            {
                var path = MatDir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.shader = Shader.Find("Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap", col); m.SetColor("_BaseColor", tint ?? Color.white);
                m.SetTexture("_BumpMap", nrm); if (nrm) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Cull", twoSided ? 0 : 2);
                if (cutout) { m.SetFloat("_AlphaClip", 1); m.SetFloat("_AlphaToMask", 1); m.SetFloat("_Cutoff", 0.33f); m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = (int)RenderQueue.AlphaTest; }
                else { m.SetFloat("_AlphaClip", 0); m.DisableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "Opaque"); m.renderQueue = -1; }
                EditorUtility.SetDirty(m); return m;
            }
            var mats = new Dictionary<string, Material>
            {
                ["DrHassan_Body"] = M("DrHassan_Body", T("Doctor/Textures/DrHassan_Body_Color"), T("Doctor/Textures/DrHassan_Body_Normal"), 0.18f),
                ["DrHassan_Head"] = M("DrHassan_Head", T("Doctor/Textures/DrHassan_Head_Color"), T("Doctor/Textures/DrHassan_Head_Normal"), 0.32f),
                ["DrHassan_HairLashes"] = M("DrHassan_HairLashes", T("Doctor/Textures/DrHassan_HairLashes_Color"), null, 0.35f, null, true, true),
                ["DrHassan_Stethoscope"] = M("DrHassan_Stethoscope", T("Doctor/Textures/DrHassan_Stethoscope_Color"), T("Doctor/Textures/DrHassan_Stethoscope_Normal"), 0.6f),
                ["DrHassan_Badge"] = M("DrHassan_Badge", T("Doctor/Textures/DrHassan_Badge"), null, 0.55f),
                ["Reyes_Body"] = M("Reyes_Body", T("Patient/Textures/Reyes_Body_Color"), T("Patient/Textures/Reyes_Body_Normal"), 0.28f),
                ["Reyes_Head"] = M("Reyes_Head", T("Patient/Textures/Reyes_Head_Color"), T("Patient/Textures/Reyes_Head_Normal"), 0.36f),
                ["Reyes_HairLashes"] = M("Reyes_HairLashes", T("Patient/Textures/Reyes_HairLashes_Color"), null, 0.3f, null, true, true),
                ["Reyes_Gown"] = M("Reyes_Gown", T("Patient/Textures/Reyes_Gown_Print"), null, 0.08f, null, false, true),
                ["Reyes_Gown_Trim"] = M("Reyes_Gown_Trim", null, null, 0.08f, new Color(0.37f, 0.60f, 0.69f), false, true),
                ["Reyes_Wristband"] = M("Reyes_Wristband", T("Patient/Textures/Reyes_Wristband"), null, 0.4f, null, false, true),
                ["Blanket_Cotton"] = M("Blanket_Cotton", null, null, 0.05f, new Color(0.42f, 0.55f, 0.66f), false, true),
            };
            AssetDatabase.SaveAssets();
            // ---------- models ----------
            foreach (var p in new[] { DoctorFbx, PatientFbx, BlanketFbx })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(p); bool isChar = p != BlanketFbx;
                mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.importBlendShapes = isChar; mi.importBlendShapeNormals = ModelImporterNormals.None; mi.importNormals = ModelImporterNormals.Import;
                mi.importAnimation = false; mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.isReadable = isChar; // zone fitting and runtime BakeMesh use the meshes
                mi.animationType = isChar ? ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
                if (isChar) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.autoGenerateAvatarMappingIfUnspecified = true; mi.optimizeGameObjects = false; }
                foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.SaveAndReimport();
                if (isChar)
                {
                    var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                    log.AppendLine(p + " avatar=" + (av ? av.name + " valid=" + av.isValid + " human=" + av.isHuman : "NONE"));
                }
            }
            // ---------- clips ----------
            var docAv = AssetDatabase.LoadAllAssetsAtPath(DoctorFbx).OfType<Avatar>().First(); var patAv = AssetDatabase.LoadAllAssetsAtPath(PatientFbx).OfType<Avatar>().First();
            // name -> (loop, loopPose, bakeXZ, firstFrame, lastFrame) ; frames < 0 = whole take
            var doc = new Dictionary<string, (bool loop, bool loopPose, bool bakeXZ, int f0, int f1)>
            {
                ["Idle_Neutral_01"] = (true, false, true, -1, -1), ["Idle_Neutral_02"] = (true, false, true, -1, -1), ["Idle_Neutral_03"] = (true, false, true, -1, -1),
                ["Idle_WeightShift"] = (true, false, true, -1, -1), ["Idle_LookAround"] = (false, false, true, -1, -1),
                ["Talk_Neutral_01"] = (true, false, true, -1, -1), ["Talk_Neutral_02"] = (true, false, true, -1, -1), ["Talk_Relaxed_01"] = (true, false, true, -1, -1),
                ["Listen_Neutral_01"] = (true, false, true, -1, -1), ["Listen_Nod_01"] = (false, false, true, -1, -1), ["Listen_Nod_02"] = (false, false, true, -1, -1),
                ["Think_01"] = (false, false, true, -1, -1), ["Point_Left"] = (false, false, true, 80, 215), ["Point_Right"] = (false, false, true, 75, 185),
                ["Invite_AfterYou"] = (false, false, true, -1, -1), ["Clipboard_Idle"] = (true, true, true, -1, -1),
                ["Walk_Slow_Loop"] = (true, true, false, -1, -1), ["Walk_Loop"] = (true, true, false, -1, -1),
            };
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir + "/Animations" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); bool patient = p.Contains("/Patient/");
                var name = Path.GetFileNameWithoutExtension(p).Split('@')[1];
                var mi = (ModelImporter)AssetImporter.GetAtPath(p);
                mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = false; mi.preserveHierarchy = true; // keep the "Bip01" node so paths match the avatar
                mi.importAnimation = true; mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false;
                mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = patient ? patAv : docAv;
                mi.animationCompression = patient ? ModelImporterAnimationCompression.Off : ModelImporterAnimationCompression.Optimal;
                mi.SaveAndReimport(); // defaultClipAnimations is only valid after the rig is known
                mi = (ModelImporter)AssetImporter.GetAtPath(p);
                var def = mi.defaultClipAnimations; if (def.Length == 0) { log.AppendLine("NO TAKE in " + p); continue; }
                var c = def[0]; c.name = patient ? "Patient_" + name : name;
                bool loop, loopPose = false, bakeXZ = true; int f0 = -1, f1 = -1;
                if (patient) loop = name.StartsWith("Reclined");
                else if (doc.TryGetValue(name, out var d)) { loop = d.loop; loopPose = d.loopPose; bakeXZ = d.bakeXZ; f0 = d.f0; f1 = d.f1; }
                else loop = false;
                if (f0 >= 0) { c.firstFrame = c.firstFrame + f0; c.lastFrame = c.firstFrame + (f1 - f0); }
                c.loopTime = loop; c.loopPose = loopPose; c.loop = loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = bakeXZ; c.keepOriginalPositionXZ = true;
                mi.clipAnimations = new[] { c };
                mi.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("clips: " + string.Join(", ", AllClips().Select(k => k.Key + (k.Value.isHumanMotion ? "" : "(!human)") + ":" + k.Value.length.ToString("F2"))));
            return log.ToString();
        }

        static Dictionary<string, AnimationClip> AllClips()
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Dir + "/Animations" }))
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview__")) d[c.name] = c;
            return d;
        }

        // =====================================================================================================
        [MenuItem("MedSim AI/Characters v2/2. Build Animator Controllers")]
        public static string BuildControllers()
        {
            var clips = AllClips(); AnimationClip C(string n) { if (!clips.TryGetValue(n, out var c)) throw new System.Exception("missing clip " + n); return c; }
            AnimatorStateTransition Tr(AnimatorState a, AnimatorState b, float dur, bool exit = false, float exitTime = 0.95f)
            { var t = a.AddTransition(b); t.duration = dur; t.hasFixedDuration = true; t.hasExitTime = exit; t.exitTime = exitTime; t.canTransitionToSelf = false; return t; }

            // ------------------------------------------------ DOCTOR ------------------------------------------------
            AssetDatabase.DeleteAsset(DoctorCtrl);
            var dc = AnimatorController.CreateAnimatorControllerAtPath(DoctorCtrl);
            dc.AddParameter("Speed", AnimatorControllerParameterType.Float); dc.AddParameter("Speaking", AnimatorControllerParameterType.Bool);
            dc.AddParameter("Listening", AnimatorControllerParameterType.Bool); dc.AddParameter("Mood", AnimatorControllerParameterType.Int);
            dc.AddParameter("Point", AnimatorControllerParameterType.Trigger);
            var sm = dc.layers[0].stateMachine;
            // idle pool: a ring, so the same idle never plays twice in a row; the long "waiting" idle (weight shifts,
            // hands on hips) is the observing idle and is visited every second step.
            var idleA = sm.AddState("Idle_A"); idleA.motion = C("Idle_Neutral_01");
            var obs1 = sm.AddState("Observe_WeightShift"); obs1.motion = C("Idle_WeightShift");
            var idleB = sm.AddState("Idle_B"); idleB.motion = C("Idle_Neutral_02");
            var look = sm.AddState("Idle_LookAround"); look.motion = C("Idle_LookAround");
            var idleC = sm.AddState("Idle_C"); idleC.motion = C("Idle_Neutral_03");
            sm.defaultState = idleA;
            var ring = new[] { idleA, obs1, idleB, look, idleC, obs1 };
            Tr(idleA, obs1, 0.6f, true, 0.96f); Tr(obs1, idleB, 0.6f, true, 0.985f); Tr(idleB, look, 0.5f, true, 0.95f); Tr(look, idleC, 0.5f, true, 0.85f); Tr(idleC, idleA, 0.6f, true, 0.96f);
            // locomotion: stride blends with speed (DoctorMovement moves the transform; root motion is off)
            var walk = sm.AddState("Walk");
            var bt = new BlendTree { name = "WalkBlend", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(bt, dc);
            bt.AddChild(C("Walk_Slow_Loop"), 0.89f); bt.AddChild(C("Walk_Loop"), 1.21f);
            walk.motion = bt;
            var listen = sm.AddState("Listen"); listen.motion = C("Listen_Neutral_01");
            var nod1 = sm.AddState("Listen_Nod_A"); nod1.motion = C("Listen_Nod_01");
            var nod2 = sm.AddState("Listen_Nod_B"); nod2.motion = C("Listen_Nod_02");
            foreach (var s in new[] { idleA, obs1, idleB, look, idleC, listen, nod1, nod2 })
            { var t = Tr(s, walk, 0.22f); t.AddCondition(AnimatorConditionMode.Greater, 0.12f, "Speed"); t.interruptionSource = TransitionInterruptionSource.Destination; }
            { var t = Tr(walk, idleA, 0.28f); t.AddCondition(AnimatorConditionMode.Less, 0.10f, "Speed"); }
            foreach (var s in new[] { idleA, obs1, idleB, look, idleC })
            { var t = Tr(s, listen, 0.35f); t.AddCondition(AnimatorConditionMode.If, 0, "Listening"); t.AddCondition(AnimatorConditionMode.Less, 0.10f, "Speed"); }
            foreach (var s in new[] { listen, nod1, nod2 }) { var t = Tr(s, idleA, 0.4f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Listening"); }
            Tr(listen, nod1, 0.25f, true, 0.16f);   // ~3.4 s of attentive listening, then a nod
            Tr(nod1, nod2, 0.25f, true, 0.9f); Tr(nod2, listen, 0.3f, true, 0.9f);

            // upper-body gesture layer (talk + point): spine, arms, fingers; legs and root stay on the base layer
            var mask = new AvatarMask { name = "UpperBodyMask" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var bp in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                mask.SetHumanoidBodyPartActive(bp, true);
            AssetDatabase.AddObjectToAsset(mask, dc);
            dc.AddLayer(new AnimatorControllerLayer { name = "UpperBody", defaultWeight = 1f, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy } });
            var usm = dc.layers[1].stateMachine; AssetDatabase.AddObjectToAsset(usm, dc);
            var empty = usm.AddState("Empty"); usm.defaultState = empty;                                 // no motion = base layer shows through
            var talkA = usm.AddState("Talk_A"); talkA.motion = C("Talk_Neutral_01"); talkA.cycleOffset = 0.07f;
            var talkB = usm.AddState("Talk_B"); talkB.motion = C("Talk_Neutral_02");
            var talkSoft = usm.AddState("Talk_Soft"); talkSoft.motion = C("Talk_Relaxed_01");
            var point = usm.AddState("Point"); // deliberately no clip: lets HumanoidLifeController's aimed point read cleanly (talk gestures pause)
            { var t = Tr(empty, talkSoft, 0.3f); t.AddCondition(AnimatorConditionMode.If, 0, "Speaking"); t.AddCondition(AnimatorConditionMode.Equals, (int)CharacterMood.Approving, "Mood"); }
            { var t = Tr(empty, talkA, 0.3f); t.AddCondition(AnimatorConditionMode.If, 0, "Speaking"); t.AddCondition(AnimatorConditionMode.NotEqual, (int)CharacterMood.Approving, "Mood"); }
            Tr(talkA, talkB, 0.5f, true, 0.45f); Tr(talkB, talkA, 0.5f, true, 0.45f);
            foreach (var s in new[] { talkA, talkB, talkSoft }) { var t = Tr(s, empty, 0.45f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Speaking"); }
            { var t = usm.AddAnyStateTransition(point); t.duration = 0.25f; t.hasExitTime = false; t.canTransitionToSelf = false; t.AddCondition(AnimatorConditionMode.If, 0, "Point"); }
            { var t = Tr(point, empty, 0.4f, true, 2.6f); }   // state has no clip: length 1 s, so exit after ~2.6 s
            EditorUtility.SetDirty(dc);

            // ------------------------------------------------ PATIENT ------------------------------------------------
            AssetDatabase.DeleteAsset(PatientCtrl);
            var pc = AnimatorController.CreateAnimatorControllerAtPath(PatientCtrl);
            pc.AddParameter("Condition", AnimatorControllerParameterType.Int);
            foreach (var b in new[] { "Speaking", "Listening" }) pc.AddParameter(b, AnimatorControllerParameterType.Bool);
            pc.AddParameter("Mood", AnimatorControllerParameterType.Int); pc.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (var t in new[] { "Point", "PainShift", "HandToChest", "Wince", "Nod", "ShakeNo", "Think" }) pc.AddParameter(t, AnimatorControllerParameterType.Trigger);
            var psm = pc.layers[0].stateMachine;
            // Idles without baked breathing: HumanoidLifeController breathes procedurally at the authored rate (phase/tempo stay tunable).
            var i0 = psm.AddState("Reclined_Idle"); i0.motion = C("Patient_Reclined_Idle_NoBreath");
            var i1 = psm.AddState("Reclined_Idle_Distress"); i1.motion = C("Patient_Reclined_Idle_Distress");
            var i2 = psm.AddState("Reclined_Idle_Severe"); i2.motion = C("Patient_Reclined_Idle_Severe");
            psm.defaultState = i0;
            var idles = new[] { i0, i1, i2 };
            for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) if (a != b) { var t = Tr(idles[a], idles[b], 1.0f); t.AddCondition(AnimatorConditionMode.Equals, b, "Condition"); }
            foreach (var (trig, clip) in new[] { ("PainShift", "Patient_PainShift"), ("HandToChest", "Patient_HandToChest"), ("Wince", "Patient_Wince") })
            {
                var s = psm.AddState(trig); s.motion = C(clip);
                var t = psm.AddAnyStateTransition(s); t.duration = 0.18f; t.hasExitTime = false; t.canTransitionToSelf = false; t.AddCondition(AnimatorConditionMode.If, 0, trig);
                for (int b = 0; b < 3; b++) { var back = Tr(s, idles[b], 0.45f, true, 0.9f); back.AddCondition(AnimatorConditionMode.Equals, b, "Condition"); }
            }
            var hmask = new AvatarMask { name = "HeadMask" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) hmask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            hmask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            AssetDatabase.AddObjectToAsset(hmask, pc);
            pc.AddLayer(new AnimatorControllerLayer { name = "Head", defaultWeight = 1f, avatarMask = hmask, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = new AnimatorStateMachine { name = "Head", hideFlags = HideFlags.HideInHierarchy } });
            var hsm = pc.layers[1].stateMachine; AssetDatabase.AddObjectToAsset(hsm, pc);
            var he = hsm.AddState("Empty"); hsm.defaultState = he;
            foreach (var (trig, clip) in new[] { ("Nod", "Patient_Nod"), ("ShakeNo", "Patient_ShakeNo"), ("Think", "Patient_Think") })
            {
                var s = hsm.AddState(trig); s.motion = C(clip);
                var t = hsm.AddAnyStateTransition(s); t.duration = 0.15f; t.hasExitTime = false; t.canTransitionToSelf = false; t.AddCondition(AnimatorConditionMode.If, 0, trig);
                Tr(s, he, 0.3f, true, 0.85f);
            }
            EditorUtility.SetDirty(pc);
            AssetDatabase.SaveAssets();
            return "controllers built: " + DoctorCtrl + " (" + dc.layers.Length + " layers), " + PatientCtrl + " (" + pc.layers.Length + " layers)";
        }

        // =====================================================================================================
        static Transform FindDeep(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static void SwapVisual(GameObject root, string fbx, RuntimeAnimatorController ctrl, System.Text.StringBuilder log)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(root), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // 1) rescue everything that is not part of the old visual: zones, snap points, voice
            var keep = new List<Transform>();
            foreach (var z in root.GetComponentsInChildren<PlacementZone>(true)) { keep.Add(z.transform); if (z.snapPoint) keep.Add(z.snapPoint); }
            foreach (var s in root.GetComponentsInChildren<CharacterSpeechController>(true)) keep.Add(s.transform);
            foreach (var k in keep) k.SetParent(root.transform, true);
            // 2) remove old visual children (MakeHuman skeleton + meshes, or a previous v2 install)
            foreach (var c in root.transform.Cast<Transform>().ToList())
            {
                if (keep.Contains(c) || c.name.StartsWith("v2_")) continue;
                bool visual = c.GetComponent<SkinnedMeshRenderer>() || c.name == "Bip01" || c.GetComponentsInChildren<Transform>(true).Length > 20;
                if (visual) Object.DestroyImmediate(c.gameObject); else log.AppendLine("  kept child " + c.name);
            }
            // 3) new visual: children of the model go directly under the existing root (Animator paths start at "Bip01")
            var tmp = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            foreach (var c in tmp.transform.Cast<Transform>().ToList()) { c.SetParent(root.transform, false); }
            Object.DestroyImmediate(tmp);
            var a = root.GetComponent<Animator>();
            a.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First(); a.runtimeAnimatorController = ctrl;
            a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; a.updateMode = AnimatorUpdateMode.Normal;
            a.Rebind();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.updateWhenOffscreen = false; r.skinnedMotionVectors = true; }
        }

        static void Rewire(GameObject root, bool gestureArm)
        {
            var a = root.GetComponent<Animator>(); var life = root.GetComponent<HumanoidLifeController>();
            var headB = a.GetBoneTransform(HumanBodyBones.Head);
            life.head = headB; life.neck = a.GetBoneTransform(HumanBodyBones.Neck);
            life.chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest); life.spine = a.GetBoneTransform(HumanBodyBones.Spine);
            life.rightUpperArm = gestureArm ? a.GetBoneTransform(HumanBodyBones.RightUpperArm) : null; life.rightLowerArm = gestureArm ? a.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;
            life.eyelids = new Transform[0]; life.face = null; life.blinkBlendShape = -1; life.faceRig = root.GetComponent<FaceRig>();
            var sp = root.GetComponentInChildren<CharacterSpeechController>(true);
            if (sp)
            {
                sp.transform.SetParent(headB, false);
                // mouth: between the mouth-corner bones if present
                var lm = FindDeep(root.transform, "Bip01 LMouthCorner"); var rm = FindDeep(root.transform, "Bip01 RMouthCorner");
                sp.transform.position = lm && rm ? (lm.position + rm.position) * 0.5f : headB.position + root.transform.forward * 0.09f;
                sp.transform.localRotation = Quaternion.identity; sp.face = life.faceRig; sp.jaw = null; sp.mouthScaleTarget = null; life.speech = sp;
                // audio-driven lip sync (uLipSync analyses this AudioSource; the driver maps phonemes to the AA_VI_* visemes)
                var lip = sp.GetComponent<uLipSync.uLipSync>(); if (!lip) lip = sp.gameObject.AddComponent<uLipSync.uLipSync>();
                lip.profile = AssetDatabase.LoadAssetAtPath<uLipSync.Profile>("Packages/com.hecomi.ulipsync/Assets/Profiles/uLipSync-Profile-Sample-" + (gestureArm ? "Female" : "Male") + ".asset");
                var drv = sp.GetComponent<ULipSyncVisemeDriver>(); if (!drv) drv = sp.gameObject.AddComponent<ULipSyncVisemeDriver>();
                drv.analyser = lip; drv.speech = sp; drv.face = life.faceRig; drv.strength = 0.6f; drv.jawOpen = 0.12f; EditorUtility.SetDirty(lip); EditorUtility.SetDirty(drv);
                EditorUtility.SetDirty(sp);
            }
            var cb = root.GetComponent<SimCharacterBase>(); if (cb) { cb.animator = a; cb.life = life; cb.speech = sp; EditorUtility.SetDirty(cb); }
            EditorUtility.SetDirty(life);
        }

        static void FitBounds(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); r.BakeMesh(m, true); var v = m.vertices; if (v.Length == 0) continue;
                var rb = r.rootBone ? r.rootBone : r.transform;
                var b = new Bounds(rb.InverseTransformPoint(r.transform.TransformPoint(v[0])), Vector3.zero);
                foreach (var p in v) b.Encapsulate(rb.InverseTransformPoint(r.transform.TransformPoint(p)));
                b.Expand(0.35f); r.localBounds = b; Object.DestroyImmediate(m);
            }
        }

        [MenuItem("MedSim AI/Characters v2/3. Install in open scene")]
        public static string InstallInScene()
        {
            var log = new System.Text.StringBuilder();
            var clips = AllClips();
            // ------------------------------------------------ DOCTOR ------------------------------------------------
            var doc = GameObject.Find(DoctorRoot);
            SwapVisual(doc, DoctorFbx, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DoctorCtrl), log);
            clips["Idle_Neutral_01"].SampleAnimation(doc, 0f);
            Rewire(doc, true); FitBounds(doc);
            var dl = doc.GetComponent<HumanoidLifeController>(); dl.lyingDown = false; dl.maxHeadYaw = 60;
            var mv = doc.GetComponent<DoctorMovement>(); if (mv) { mv.animator = doc.GetComponent<Animator>(); mv.walkSpeed = 1.05f; EditorUtility.SetDirty(mv); }
            var cap = doc.GetComponent<CapsuleCollider>(); if (cap) { cap.height = 1.72f; cap.center = new Vector3(0, 0.86f, 0); cap.radius = 0.24f; }
            log.AppendLine("doctor installed: human=" + doc.GetComponent<Animator>().isHuman);

            // ------------------------------------------------ PATIENT ------------------------------------------------
            var pat = GameObject.Find(PatientRoot);
            SwapVisual(pat, PatientFbx, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PatientCtrl), log);
            pat.transform.SetPositionAndRotation(new Vector3(BedCentreX, MattressTopY, BackrestHingeZ + RootToHinge), Quaternion.Euler(0, 180, 0));
            clips["Patient_Reclined_Base"].SampleAnimation(pat, 0f);
            Rewire(pat, false); FitBounds(pat);
            var pl = pat.GetComponent<HumanoidLifeController>(); pl.lyingDown = true; pl.maxHeadYaw = 45; pl.maxHeadPitch = 20; pl.breathsPerMinute = 20;
            var a = pat.GetComponent<Animator>(); Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            // capsules that keep the player's hands/instruments out of the body
            void Capsule(string name, Vector3 p0, Vector3 p1, float radius)
            {
                var t = pat.transform.Find(name); if (!t) return; var c = t.GetComponent<CapsuleCollider>(); if (!c) return;
                t.position = (p0 + p1) * 0.5f; t.rotation = Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized); t.localScale = Vector3.one;
                c.direction = 1; c.center = Vector3.zero; c.radius = radius; c.height = (p1 - p0).magnitude + radius * 2;
            }
            Capsule("v2_Body_Torso", B(HumanBodyBones.Hips).position, B(HumanBodyBones.Neck).position, 0.15f);
            Capsule("v2_Body_Legs", B(HumanBodyBones.Hips).position, (B(HumanBodyBones.LeftFoot).position + B(HumanBodyBones.RightFoot).position) * 0.5f, 0.16f);
            log.AppendLine("patient installed: human=" + a.isHuman + " head=" + B(HumanBodyBones.Head).position.ToString("F3"));

            // blanket: ours is fitted to this pose; the environment one was fitted to the old MakeHuman body
            var parent = pat.transform.parent; var old = parent.Find("Patient Blanket"); if (old) Object.DestroyImmediate(old.gameObject);
            var bl = new GameObject("Patient Blanket"); bl.transform.SetParent(parent, false);
            bl.transform.SetPositionAndRotation(pat.transform.position, pat.transform.rotation);
            var blMesh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BlanketFbx)); blMesh.name = "Blanket Mesh"; blMesh.transform.SetParent(bl.transform, false); // keeps the FBX axis-conversion rotation
            foreach (var r in bl.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Blanket_Cotton.mat"); r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            var bed = GameObject.Find("Hospital Bed");
            var envBlanket = bed ? bed.transform.Find("v2_Blanket") : null;
            if (envBlanket)
            {
                // reuse the environment's cellular-blanket material so the bed linen stays one family
                var envMat = envBlanket.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m);
                if (envMat) foreach (var r in bl.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = envMat;
                envBlanket.gameObject.SetActive(false); log.AppendLine("disabled Hospital Bed/v2_Blanket (fitted to the old body); material reused: " + (envMat ? envMat.name : "none"));
            }

            log.AppendLine(FitZones());
            log.AppendLine(FitPillow());
            EditorUtility.SetDirty(pat); EditorUtility.SetDirty(doc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pat.scene);
            return log.ToString();
        }

        /// <summary>Puts the pillow under the back of the head (pose sampled at Reclined_Base).</summary>
        public static string FitPillow()
        {
            var pat = GameObject.Find(PatientRoot); var a = pat.GetComponent<Animator>();
            var bed = GameObject.Find("Hospital Bed"); var hs = bed.transform.Find("Head_Section"); var pillow = hs ? hs.Find("v2_Pillow") : null;
            if (!pillow) return "pillow not found";
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            // backrest surface normal (out of the mattress) and up-the-bed direction
            Vector3 n = hs.up, along = hs.forward; if (along.y < 0) along = -along;
            // back of the head: furthest head-skinned point against the backrest normal
            var smr = pat.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.sharedMesh.blendShapeCount > 0);
            var m = new Mesh(); smr.BakeMesh(m, true); var verts = m.vertices; var bw = smr.sharedMesh.boneWeights; int hi = System.Array.IndexOf(smr.bones, head);
            Vector3 occ = head.position; float best = float.MaxValue; float topAlong = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = bw[i]; if (!(w.boneIndex0 == hi && w.weight0 > 0.6f)) continue;
                var p = smr.transform.TransformPoint(verts[i]); float d = Vector3.Dot(p, n);
                if (d < best) { best = d; occ = p; }
                topAlong = Mathf.Max(topAlong, Vector3.Dot(p, along));
            }
            Object.DestroyImmediate(m);
            var rends = pillow.GetComponentsInChildren<Renderer>(); if (rends.Length == 0) return "pillow has no renderer";
            // pillow thickness along the backrest normal, measured from its mesh in world space
            float minN = float.MaxValue, maxN = float.MinValue;
            foreach (var mf in pillow.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) { float d = Vector3.Dot(mf.transform.TransformPoint(v), n); minN = Mathf.Min(minN, d); maxN = Mathf.Max(maxN, d); }
            float thick = maxN - minN;
            var centre = rends.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }).center;
            // target: pillow's top surface 2.5 cm past the back of the head (head sinks in), centred under the head, lying on the backrest
            float surfaceD = Vector3.Dot(hs.position, n) + 0.075f;      // mattress top plane (mattress is 0.15 thick around the pivot)
            float wantCentreN = Mathf.Max(surfaceD + thick * 0.5f - 0.01f, best + 0.025f - thick * 0.5f);
            Vector3 target = occ + along * 0.02f; target += n * (wantCentreN - Vector3.Dot(target, n)); target.x = hs.position.x;
            pillow.position += target - centre;
            EditorUtility.SetDirty(pillow);
            return "pillow: thickness " + thick.ToString("F3") + " head-back " + occ.ToString("F3") + " gap head->mattress " + (best - surfaceD).ToString("F3") + " centre " + target.ToString("F3");
        }

        /// <summary>Re-fits the 10 existing PlacementZones to the v2 body surface (pose sampled at Reclined_Base).</summary>
        [MenuItem("MedSim AI/Characters v2/4. Re-fit examination zones")]
        public static string FitZones()
        {
            var go = GameObject.Find(PatientRoot); var a = go.GetComponent<Animator>(); Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            var zones = go.GetComponentsInChildren<PlacementZone>(true).ToDictionary(z => z.zoneId);
            var temp = new List<GameObject>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.name.Contains("Wristband")) continue;
                var m = new Mesh(); smr.BakeMesh(m, true);
                var t = new GameObject("TEMP_COLLIDER_" + smr.name); t.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                t.AddComponent<MeshCollider>().sharedMesh = m; temp.Add(t);
            }
            Physics.SyncTransforms();
            var upper = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest); var hips = B(HumanBodyBones.Hips);
            var lUA = B(HumanBodyBones.LeftUpperArm); var rUA = B(HumanBodyBones.RightUpperArm);
            Vector3 up = (B(HumanBodyBones.Neck).position - hips.position).normalized;
            Vector3 left = (lUA.position - rUA.position).normalized;
            Vector3 ant = Vector3.Cross(left, up).normalized; if (Vector3.Dot(ant, go.transform.forward + Vector3.up) < 0) ant = -ant;
            Vector3 shoulderMid = (lUA.position + rUA.position) * 0.5f;
            var sb = new System.Text.StringBuilder("zones:");
            Vector3 Surface(Vector3 p, out Vector3 normal)
            {
                var origin = p + ant * 0.40f;
                var hits = Physics.RaycastAll(origin, -ant, 0.7f).Where(h => h.collider.name.StartsWith("TEMP_COLLIDER")).OrderBy(h => h.distance).ToArray();
                if (hits.Length > 0) { normal = hits[0].normal; return hits[0].point + hits[0].normal * 0.004f; }
                normal = ant; return p + ant * 0.1f;
            }
            void Place(string id, Transform bone, Vector3 pos, Quaternion rot, Transform snapParent = null, Vector3? snapPos = null, Quaternion? snapRot = null)
            {
                if (!zones.TryGetValue(id, out var z)) { sb.Append(" MISSING " + id); return; }
                z.transform.SetParent(bone, true); z.transform.SetPositionAndRotation(pos, rot); z.transform.localScale = Vector3.one;
                if (z.snapPoint) { z.snapPoint.SetParent(snapParent ? snapParent : bone, true); z.snapPoint.SetPositionAndRotation(snapPos ?? pos, snapRot ?? rot); z.snapPoint.localScale = Vector3.one; }
                EditorUtility.SetDirty(z); sb.Append(" " + id + pos.ToString("F2"));
            }
            // chest landmarks relative to the shoulder line (metres down the trunk, metres toward the patient's left)
            var chest = new (string id, float down, float lat)[]
            {
                ("CHEST_AORTIC", 0.075f, -0.035f), ("CHEST_PULMONARY", 0.075f, 0.035f), ("CHEST_TRICUSPID", 0.165f, 0.022f),
                ("CHEST_MITRAL", 0.195f, 0.085f), ("CHEST_LUNG_R", 0.12f, -0.105f), ("CHEST_LUNG_L", 0.12f, 0.115f),
            };
            foreach (var c in chest)
            {
                var s = Surface(shoulderMid - up * c.down + left * c.lat, out var n);
                Place(c.id, upper, s, Quaternion.LookRotation(n, up));
            }
            foreach (var (uaB, laB, idxB, id) in new[] { (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftIndexDistal, "L"), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightIndexDistal, "R") })
            {
                var ua = B(uaB); var la = B(laB);
                var armDir = (la.position - ua.position).normalized; var mid = Vector3.Lerp(ua.position, la.position, 0.5f);
                var rot = Quaternion.FromToRotation(Vector3.up, armDir);
                Place("ARM_UPPER_" + id, ua, mid, rot, ua, mid, rot);
                var d = B(idxB); var inter = d.parent; var fdir = (d.position - inter.position).normalized;
                var tip = d.position + fdir * 0.010f;
                // nail side of the finger = away from the palm; approximate with the hand's back direction
                var hand = inter.parent.parent; var side = Vector3.Cross(fdir, (B(id == "L" ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal).position - inter.parent.position).normalized).normalized;
                if (Vector3.Dot(side, Vector3.up) < 0) side = -side;
                var frot = Quaternion.LookRotation(fdir, side);
                Place("FINGER_" + id, d, tip, frot, d, tip, frot);
            }
            foreach (var t in temp) Object.DestroyImmediate(t);
            return sb.ToString();
        }

        [MenuItem("MedSim AI/Characters v2/Run all")]
        public static void RunAll() { Debug.Log(ConfigureImports()); Debug.Log(BuildControllers()); Debug.Log(InstallInScene()); }
    }
}
