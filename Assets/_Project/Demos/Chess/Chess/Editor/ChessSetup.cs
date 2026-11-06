using CardboardXR.Demos;
using CardboardXR.Demos.Chess;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // Chess: its app library entry, the board prefab (built out in code when placed, with its
    // materials) and its starter scene (see DemoSetup). The build adds the entry and makes the scene
    // once, then never overwrites either.
    class ChessSetup : DemoSetup
    {
        public override string Name => "Chess";

        public override DemoCatalog.Entry Entry => new()
        {
            Id = "chess",
            Title = "Chess",
            Description = "Play chess with a friend on a surface of your choice, taking turns with gaze and pinch.",
            Tagline = "Chess on your own table",
            Icon = "grid_view",
            Tint = new Color(0.55f, 0.40f, 0.25f),
            Requirements = new DemoRequirements { SixDof = Need.On, Passthrough = Need.On, Hands = Need.Off, ScanSurfaces = true },
        };

        public override void BuildScene(string folder)
        {
            var unlit = DemoAssets.Unlit;
            Material Make(string name, Color color) => StereoBuilder.CreateMaterial(name, unlit, color, null, 1f, folder);

            // Unlit, so the colors are what's seen: wood squares, ivory and charcoal pieces, and
            // markers that stand out from both squares.
            var board = new GameObject("Chess Board").AddComponent<ChessBoard>();
            var serialized = new SerializedObject(board);
            serialized.FindProperty("m_Light").objectReferenceValue = Make("Square Light", new Color(0.93f, 0.82f, 0.62f));
            serialized.FindProperty("m_Dark").objectReferenceValue = Make("Square Dark", new Color(0.46f, 0.30f, 0.18f));
            serialized.FindProperty("m_White").objectReferenceValue = Make("Piece White", new Color(0.97f, 0.95f, 0.88f));
            serialized.FindProperty("m_Black").objectReferenceValue = Make("Piece Black", new Color(0.13f, 0.13f, 0.15f));
            serialized.FindProperty("m_Select").objectReferenceValue = Make("Marker Select", new Color(0.30f, 0.60f, 1f));
            serialized.FindProperty("m_Target").objectReferenceValue = Make("Marker Target", new Color(0.35f, 0.85f, 0.45f));
            serialized.FindProperty("m_Check").objectReferenceValue = Make("Marker Check", new Color(0.95f, 0.25f, 0.25f));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(board.gameObject, $"{folder}/Chess Board.prefab");
            Object.DestroyImmediate(board.gameObject);

            var demo = new GameObject("Chess Demo").AddComponent<ChessDemo>();
            var serializedDemo = new SerializedObject(demo);
            serializedDemo.FindProperty("m_BoardPrefab").objectReferenceValue = prefab;
            serializedDemo.ApplyModifiedPropertiesWithoutUndo();

            // What the demo shows in VR, which passthrough hides: a dark floor with a faint grid every
            // half meter, for a sense of space and scale.
            var environment = DemoAssets.Environment();
            var grid = StereoBuilder.SaveAsset(DemoAssets.CreateGridTexture(), $"{folder}/Grid.asset");
            const float floorSize = 30f;
            DemoAssets.Floor(environment.transform,
                StereoBuilder.CreateMaterial("Chess Floor", unlit, Color.white, grid, floorSize / 0.5f, folder), floorSize);
        }
    }
}
