using System;
using UnityEditor;

namespace MedSimAI.Hospital.EditorTools
{
    /// <summary>
    /// CI entry point (GitHub Actions, batch mode): the SAME integrated builds as the menu items, written inside the
    /// project folder so the workflow can upload them. Uses the committed scenes; regenerates nothing.
    /// </summary>
    public static class HospitalBuildCI
    {
        public static void BuildBoth()
        {
            int code = 0;
            try
            {
                var d = HospitalBuild.BuildDesktop("build/MedSimAI_Integrated_Desktop/MedSimAI.exe"); UnityEngine.Debug.Log("[HospitalBuildCI] " + d);
                if (!d.Contains("Succeeded")) code = 1;
                var p = HospitalBuild.BuildPcvr("build/MedSimAI_Integrated_PCVR/MedSimAI.exe"); UnityEngine.Debug.Log("[HospitalBuildCI] " + p);
                if (!p.Contains("Succeeded")) code = 1;
            }
            catch (Exception e) { UnityEngine.Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
