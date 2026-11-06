using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Demos.Chess
{
    // The chess board in the room, built out in code when the placer puts the prefab on a surface (the
    // way PortalWorld is): 64 squares the gaze can hit, the pieces on them from simple shapes, and
    // markers for the chosen piece, where it can go, and a king in check. Nothing here knows the
    // rules; ChessDemo tells it what stands where. In the board's own space the origin is the middle
    // of the board on the surface, files run along +x (a to h), and white's side is +z, toward whoever
    // placed it (see ChessRules.SquareCenter).
    public class ChessBoard : MonoBehaviour
    {
        // Squares of 5 cm make a board 40 cm across, about what fits on a coffee table.
        public const float k_SquareSize = 0.05f;
        const float k_BoardThickness = 0.012f;

        [SerializeField] Material m_Light;
        [SerializeField] Material m_Dark;
        [SerializeField] Material m_White;
        [SerializeField] Material m_Black;
        [SerializeField] Material m_Select;
        [SerializeField] Material m_Target;
        [SerializeField] Material m_Check;

        readonly Transform[] m_SquareObjects = new Transform[64];
        readonly Renderer[] m_Markers = new Renderer[64];
        readonly Dictionary<Transform, int> m_SquareOf = new();
        // The pieces' roots, by the square they stand on. Rebuilt on every Show.
        readonly Dictionary<Transform, int> m_PieceSquare = new();
        readonly List<GameObject> m_Pieces = new();
        Transform m_PieceRoot;

        void Awake()
        {
            BuildSquares();
            m_PieceRoot = new GameObject("Pieces").transform;
            m_PieceRoot.SetParent(transform, false);
        }

        // The square the gaze is on, if it's on this board: a square, or a piece standing on one.
        // -1 for anything else.
        public int SquareOf(GameObject target)
        {
            for (var t = target != null ? target.transform : null; t != null; t = t.parent)
            {
                if (m_PieceSquare.TryGetValue(t, out var onSquare))
                    return onSquare;
                if (m_SquareOf.TryGetValue(t, out var square))
                    return square;
                if (t == transform)
                    break;
            }
            return -1;
        }

        // Draws the position: every piece where it stands.
        public void Show(ChessPosition position)
        {
            // Destroy waits for the end of the frame, so hide the old pieces now, or the gaze could
            // still hit one.
            foreach (var piece in m_Pieces)
            {
                piece.SetActive(false);
                Destroy(piece);
            }
            m_Pieces.Clear();
            m_PieceSquare.Clear();
            for (var square = 0; square < 64; square++)
            {
                var piece = position[square];
                if (piece.IsNone)
                    continue;
                var made = BuildPiece(piece);
                made.name = $"{ChessRules.SideName(piece.Side)} {piece.Kind} {ChessRules.Name(square)}";
                var (x, z) = ChessRules.SquareCenter(square, k_SquareSize);
                made.transform.SetParent(m_PieceRoot, false);
                made.transform.localPosition = new Vector3(x, k_BoardThickness, z);
                // Knights face the other side's end of the board.
                made.transform.localRotation = Quaternion.Euler(0f, piece.Side == Side.White ? 180f : 0f, 0f);
                // The gaze finds the piece's collider, and the square under it is what it means.
                m_PieceSquare[made.transform] = square;
                m_Pieces.Add(made);
            }
        }

        // Marks the chosen square, the squares it can move to, and a king in check (-1 for none).
        public void Mark(int selected, IReadOnlyList<int> targets, int checkedKing)
        {
            for (var i = 0; i < 64; i++)
                m_Markers[i].enabled = false;
            if (checkedKing >= 0)
                SetMarker(checkedKing, m_Check);
            if (selected >= 0)
                SetMarker(selected, m_Select);
            if (targets != null)
            {
                for (var i = 0; i < targets.Count; i++)
                    SetMarker(targets[i], m_Target);
            }
        }

        void SetMarker(int square, Material material)
        {
            m_Markers[square].sharedMaterial = material;
            m_Markers[square].enabled = true;
        }

        void BuildSquares()
        {
            // The frame under the squares, a little larger than them.
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(transform, false);
            frame.transform.localScale = new Vector3(k_SquareSize * 8.6f, k_BoardThickness * 0.8f, k_SquareSize * 8.6f);
            frame.transform.localPosition = new Vector3(0f, k_BoardThickness * 0.4f, 0f);
            frame.GetComponent<Renderer>().sharedMaterial = m_Dark;

            for (var square = 0; square < 64; square++)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"Square {ChessRules.Name(square)}";
                tile.transform.SetParent(transform, false);
                var (x, z) = ChessRules.SquareCenter(square, k_SquareSize);
                tile.transform.localScale = new Vector3(k_SquareSize, k_BoardThickness, k_SquareSize);
                tile.transform.localPosition = new Vector3(x, k_BoardThickness * 0.5f, z);
                // a1 is dark, as on every board.
                var dark = (ChessPosition.FileOf(square) + ChessPosition.RankOf(square)) % 2 == 0;
                tile.GetComponent<Renderer>().sharedMaterial = dark ? m_Dark : m_Light;
                m_SquareObjects[square] = tile.transform;
                m_SquareOf[tile.transform] = square;

                // A flat disc just above the square, shown or hidden by Mark.
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Marker";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(tile.transform, false);
                // The tile is squashed, so counter it: a 4 cm wide, 3 mm thick disc in board space,
                // mostly above the square.
                marker.transform.localScale = new Vector3(0.8f, 0.0015f / k_BoardThickness, 0.8f);
                marker.transform.localPosition = new Vector3(0f, 0.5f + 0.0008f / k_BoardThickness, 0f);
                var renderer = marker.GetComponent<Renderer>();
                renderer.enabled = false;
                m_Markers[square] = renderer;
            }
        }

        // A piece from simple shapes, about 3 to 5 cm tall, by kind. Only the root is parented by the
        // caller. Each shape's collider is kept, so the gaze can hit the piece itself.
        GameObject BuildPiece(Piece piece)
        {
            var material = piece.Side == Side.White ? m_White : m_Black;
            var root = new GameObject();
            const float s = k_SquareSize;
            switch (piece.Kind)
            {
                case PieceKind.Pawn:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.5f * s, 0.25f * s, 0.5f * s), 0.25f * s);
                    Part(root, PrimitiveType.Sphere, material, Vector3.one * 0.38f * s, 0.62f * s);
                    break;
                case PieceKind.Rook:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.6f * s, 0.4f * s, 0.6f * s), 0.4f * s);
                    Part(root, PrimitiveType.Cube, material, new Vector3(0.7f * s, 0.2f * s, 0.7f * s), 0.9f * s);
                    break;
                case PieceKind.Knight:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.6f * s, 0.25f * s, 0.6f * s), 0.25f * s);
                    Part(root, PrimitiveType.Cube, material, new Vector3(0.3f * s, 0.8f * s, 0.3f * s), 0.65f * s);
                    var head = Part(root, PrimitiveType.Cube, material, new Vector3(0.28f * s, 0.28f * s, 0.62f * s), 1.0f * s);
                    head.transform.localPosition += new Vector3(0f, 0f, 0.18f * s);
                    break;
                case PieceKind.Bishop:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.6f * s, 0.25f * s, 0.6f * s), 0.25f * s);
                    Part(root, PrimitiveType.Capsule, material, new Vector3(0.42f * s, 0.6f * s, 0.42f * s), 0.85f * s);
                    Part(root, PrimitiveType.Sphere, material, Vector3.one * 0.2f * s, 1.55f * s);
                    break;
                case PieceKind.Queen:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.7f * s, 0.25f * s, 0.7f * s), 0.25f * s);
                    Part(root, PrimitiveType.Capsule, material, new Vector3(0.5f * s, 0.85f * s, 0.5f * s), 1.0f * s);
                    Part(root, PrimitiveType.Sphere, material, Vector3.one * 0.3f * s, 1.9f * s);
                    break;
                default:
                    Part(root, PrimitiveType.Cylinder, material, new Vector3(0.7f * s, 0.25f * s, 0.7f * s), 0.25f * s);
                    Part(root, PrimitiveType.Capsule, material, new Vector3(0.5f * s, 0.9f * s, 0.5f * s), 1.05f * s);
                    Part(root, PrimitiveType.Cube, material, new Vector3(0.1f * s, 0.4f * s, 0.1f * s), 2.1f * s);
                    Part(root, PrimitiveType.Cube, material, new Vector3(0.3f * s, 0.1f * s, 0.1f * s), 2.1f * s);
                    break;
            }
            return root;
        }

        // One shape of a piece: a primitive of this size (primitives are 1 m across, a cylinder or
        // capsule 2 m tall), standing with its middle at this height above the square.
        static GameObject Part(GameObject root, PrimitiveType type, Material material, Vector3 size, float height)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(root.transform, false);
            part.transform.localScale = size;
            part.transform.localPosition = new Vector3(0f, height, 0f);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
    }
}
