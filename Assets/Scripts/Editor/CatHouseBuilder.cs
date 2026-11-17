using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 고양이 집 만들기] 한 번으로 고양이 집 프리팹을 만든다.
/// Unity 기본 도형(큐브 · 원기둥)을 조합한 작은 집이며, 책상 위에 올라가는 크기(가로 약 14cm)다.
///
/// - 집 앞쪽(문)은 로컬 +Z 방향. CatHousePlacer가 집을 플레이어 쪽으로 돌려 놓으므로 문이 플레이어를 본다.
/// - 충돌체는 모두 제거한다. (놓을 위치를 찾는 레이를 가리지 않도록)
/// - 결과: Assets/Prefabs/CatHouse.prefab, 머티리얼은 Assets/Prefabs/CatHouseMaterials/
/// - 씬에 CatHousePlacer가 있으면 House Prefab 칸에 자동으로 넣는다.
/// 다시 실행하면 같은 경로의 프리팹을 덮어쓴다. 크기나 색은 아래 상수를 바꾸고 다시 실행하면 된다.
/// </summary>
public static class CatHouseBuilder
{
    const string k_PrefabFolder = "Assets/Prefabs";
    const string k_MaterialFolder = "Assets/Prefabs/CatHouseMaterials";
    const string k_PrefabPath = k_PrefabFolder + "/CatHouse.prefab";

    // 크기 (미터)
    const float k_Width = 0.14f;      // 집 가로
    const float k_Depth = 0.12f;      // 집 세로
    const float k_WallHeight = 0.09f; // 벽 높이
    const float k_RoofRise = 0.05f;   // 지붕 높이
    const float k_Thick = 0.008f;     // 판 두께

    // 색
    static readonly Color k_WallColor = new Color(1.00f, 0.90f, 0.80f);   // 크림
    static readonly Color k_RoofColor = new Color(0.93f, 0.48f, 0.42f);   // 코랄
    static readonly Color k_DoorColor = new Color(0.30f, 0.20f, 0.17f);   // 어두운 갈색 (문 구멍처럼 보이게)
    static readonly Color k_CushionColor = new Color(0.62f, 0.85f, 0.78f); // 민트
    static readonly Color k_BaseColor = new Color(0.82f, 0.66f, 0.50f);   // 나무 바닥

    [MenuItem("Tools/Here Kitty/고양이 집 만들기")]
    public static void Build()
    {
        EnsureFolder(k_PrefabFolder);
        EnsureFolder(k_MaterialFolder);

        var wall = MakeMaterial("CatHouse_Wall", k_WallColor);
        var roof = MakeMaterial("CatHouse_Roof", k_RoofColor);
        var door = MakeMaterial("CatHouse_Door", k_DoorColor);
        var cushion = MakeMaterial("CatHouse_Cushion", k_CushionColor);
        var floor = MakeMaterial("CatHouse_Base", k_BaseColor);

        var root = new GameObject("CatHouse");

        // 바닥 받침 (책상에 닿는 면이 y = 0)
        AddCube(root, "Base", new Vector3(0f, k_Thick * 0.5f, 0f),
            new Vector3(k_Width + 0.02f, k_Thick, k_Depth + 0.02f), floor);

        float wallY = k_Thick + k_WallHeight * 0.5f;

        // 몸통: 뒤 · 왼 · 오른 벽 (앞쪽은 문 양옆 벽 + 문 위 벽으로 구멍을 남긴다)
        AddCube(root, "Wall_Back", new Vector3(0f, wallY, -k_Depth * 0.5f + k_Thick * 0.5f),
            new Vector3(k_Width, k_WallHeight, k_Thick), wall);
        AddCube(root, "Wall_Left", new Vector3(-k_Width * 0.5f + k_Thick * 0.5f, wallY, 0f),
            new Vector3(k_Thick, k_WallHeight, k_Depth), wall);
        AddCube(root, "Wall_Right", new Vector3(k_Width * 0.5f - k_Thick * 0.5f, wallY, 0f),
            new Vector3(k_Thick, k_WallHeight, k_Depth), wall);

        // 앞벽: 가운데에 문 구멍 (가로 6cm, 높이 6cm)
        float doorW = 0.06f, doorH = 0.06f;
        float frontZ = k_Depth * 0.5f - k_Thick * 0.5f;
        float sideW = (k_Width - doorW) * 0.5f;
        AddCube(root, "Wall_Front_L", new Vector3(-(doorW + sideW) * 0.5f, wallY, frontZ),
            new Vector3(sideW, k_WallHeight, k_Thick), wall);
        AddCube(root, "Wall_Front_R", new Vector3((doorW + sideW) * 0.5f, wallY, frontZ),
            new Vector3(sideW, k_WallHeight, k_Thick), wall);
        float topH = k_WallHeight - doorH;
        AddCube(root, "Wall_Front_Top", new Vector3(0f, k_Thick + doorH + topH * 0.5f, frontZ),
            new Vector3(doorW, topH, k_Thick), wall);

        // 문 안쪽을 어둡게 (구멍 안이 그늘져 보이게 하는 얇은 판)
        AddCube(root, "Door_Shadow", new Vector3(0f, k_Thick + doorH * 0.5f, frontZ - 0.012f),
            new Vector3(doorW, doorH, 0.002f), door);

        // 동그란 문 위쪽 장식 (아치처럼 보이게 반쯤 묻힌 원기둥)
        var arch = AddCylinder(root, "Door_Arch", new Vector3(0f, k_Thick + doorH, frontZ + 0.001f),
            new Vector3(doorW, k_Thick * 0.5f, doorW), door);
        arch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // 지붕: 경사판 두 장. 처마가 벽 밖으로 halfW - wallHalf 만큼 튀어나온다.
        //   지붕선(판의 중심선): (±halfW, roofLineY) → (0, roofLineY + k_RoofRise)
        //   벽 바깥 끝(x = ±wallHalf)에서 지붕선 높이가 정확히 벽 꼭대기(wallTopY)가 되도록 지붕을 eaveH만큼 내린다.
        //   → 옆벽과 지붕 사이에 틈이 생기지 않는다.
        float wallHalf = k_Width * 0.5f;
        float halfW = wallHalf + 0.012f;
        float eaveH = k_RoofRise * (1f - wallHalf / halfW);
        float wallTopY = k_Thick + k_WallHeight;
        float roofLineY = wallTopY - eaveH;
        float slopeLen = Mathf.Sqrt(halfW * halfW + k_RoofRise * k_RoofRise);
        float angle = Mathf.Atan2(k_RoofRise, halfW) * Mathf.Rad2Deg;
        var roofSize = new Vector3(slopeLen, k_Thick, k_Depth + 0.025f);
        var roofL = AddCube(root, "Roof_L", new Vector3(-halfW * 0.5f, roofLineY + k_RoofRise * 0.5f, 0f), roofSize, roof);
        roofL.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        var roofR = AddCube(root, "Roof_R", new Vector3(halfW * 0.5f, roofLineY + k_RoofRise * 0.5f, 0f), roofSize, roof);
        roofR.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);

        // 지붕 아래 앞 · 뒤 삼각형 벽 (박공). 벽 꼭대기 양 끝에서 지붕 꼭대기까지, 지붕선과 같은 기울기의 판 메시.
        var gableMesh = GetGableMesh(wallHalf, 0f, k_RoofRise - eaveH, k_Thick);
        float roofBaseY = wallTopY;
        for (int i = 0; i < 2; i++)
        {
            float z = (i == 0 ? 1f : -1f) * (k_Depth * 0.5f - k_Thick * 0.5f);
            var gable = new GameObject(i == 0 ? "Gable_Front" : "Gable_Back");
            gable.transform.SetParent(root.transform, false);
            gable.transform.localPosition = new Vector3(0f, roofBaseY, z);
            gable.AddComponent<MeshFilter>().sharedMesh = gableMesh;
            gable.AddComponent<MeshRenderer>().sharedMaterial = wall;
        }

        // 문 앞 방석 (고양이가 앉을 자리)
        AddCylinder(root, "Cushion", new Vector3(0f, k_Thick + 0.004f, k_Depth * 0.5f + 0.045f),
            new Vector3(0.075f, 0.004f, 0.075f), cushion);

        // 저장
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
        Object.DestroyImmediate(root);

        AssignToPlacer(prefab);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[CatHouseBuilder] 고양이 집 프리팹을 만들었어요: {k_PrefabPath}");
    }

    static GameObject AddCube(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
        => AddPrimitive(PrimitiveType.Cube, parent, name, pos, size, mat);

    static GameObject AddCylinder(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
        => AddPrimitive(PrimitiveType.Cylinder, parent, name, pos, size, mat);

    static GameObject AddPrimitive(PrimitiveType type, GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        // 원기둥 기본 높이는 2라서 y를 절반으로 맞춘다
        go.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>
    /// 박공용 오각형 판 메시 (두께 thick, 로컬 z 가운데 정렬).
    /// 꼭짓점: (-w,0) (w,0) (w,eaveH) (0,apexH) (-w,eaveH)
    /// 프리팹에서 쓰려면 메시가 에셋으로 저장돼 있어야 해서 머티리얼 폴더에 저장한다.
    /// </summary>
    static Mesh GetGableMesh(float w, float eaveH, float apexH, float thick)
    {
        // eaveH가 0이면 겹치는 점이 생기므로 삼각형으로 만든다.
        var outline = eaveH > 0.0001f
            ? new[] { new Vector2(-w, 0f), new Vector2(w, 0f), new Vector2(w, eaveH), new Vector2(0f, apexH), new Vector2(-w, eaveH) }
            : new[] { new Vector2(-w, 0f), new Vector2(w, 0f), new Vector2(0f, apexH) };
        float zf = thick * 0.5f, zb = -thick * 0.5f;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        // 앞면(+z)과 뒷면(-z): 볼록 오각형이라 첫 점 기준 부채꼴로 삼각형을 만든다
        void Face(float z, bool front)
        {
            int start = verts.Count;
            foreach (var p in outline)
                verts.Add(new Vector3(p.x, p.y, z));
            for (int i = 1; i < outline.Length - 1; i++)
            {
                // Unity는 바라보는 쪽에서 시계 방향인 면이 앞면. 외곽선이 반시계(x 오른쪽, y 위)라서
                // +z 면은 그대로, -z 면은 순서를 뒤집는다.
                if (front) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
                else { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
            }
        }
        Face(zf, true);
        Face(zb, false);

        // 옆면 (테두리를 따라 사각형)
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            int s = verts.Count;
            verts.Add(new Vector3(a.x, a.y, zf));
            verts.Add(new Vector3(b.x, b.y, zf));
            verts.Add(new Vector3(b.x, b.y, zb));
            verts.Add(new Vector3(a.x, a.y, zb));
            // 바깥쪽을 향하도록 (반시계 외곽선의 바깥 법선 방향)
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
            tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
        }

        var mesh = new Mesh { name = "CatHouse_Gable" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string path = $"{k_MaterialFolder}/CatHouse_Gable.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Material MakeMaterial(string name, Color color)
    {
        string path = $"{k_MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void AssignToPlacer(GameObject prefab)
    {
        var placer = Object.FindFirstObjectByType<CatHousePlacer>();
        if (placer == null)
            return;
        var so = new SerializedObject(placer);
        so.FindProperty("m_HousePrefab").objectReferenceValue = prefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(placer);
        Debug.Log("[CatHouseBuilder] 씬의 CatHousePlacer > House Prefab 칸에 연결했어요. 씬을 저장하세요.");
    }
}
