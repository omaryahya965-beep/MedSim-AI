using UnityEngine; using UnityEditor;
public static class CharPreview {
  public static string Run() {
    var root = new GameObject("TEMP_PREVIEW"); root.transform.position = new Vector3(20, 0, 0);
    var d = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/DrSaraHassan/DrSaraHassan.prefab"), root.transform);
    d.transform.localPosition = new Vector3(-0.5f, 0, 0); d.transform.localRotation = Quaternion.Euler(0, 180, 0);
    var p = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/AdamReyes/AdamReyes.prefab"), root.transform);
    p.transform.localPosition = new Vector3(0.5f, 0, 0); p.transform.localRotation = Quaternion.Euler(0, 180, 0);
    var l = new GameObject("k").AddComponent<Light>(); l.transform.SetParent(root.transform); l.type = LightType.Directional; l.intensity = 1.2f; l.transform.rotation = Quaternion.Euler(30, 160, 0);
    var r1 = Shot.Take("P2_char_full", 20, 1.1f, -3.2f, 20, 0.95f, 0, 38);
    var r2 = Shot.Take("P2_char_faces", 20, 1.6f, -1.1f, 20, 1.58f, 0, 32);
    Object.DestroyImmediate(root);
    return r1 + " " + r2;
  }
}
