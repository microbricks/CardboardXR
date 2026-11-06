using System.Collections.Generic;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos.Chess
{
    // Chess: two people play on a surface of their choice, taking turns on one phone. Look at a table
    // (or the floor) and pinch to put the board there, anchored to it. Then everything is gaze and
    // pinch, with no hand position needed: look at one of your pieces and pinch to choose it, its legal
    // squares light up, then look at one of them and pinch to move. Looking at another of your own
    // pieces and pinching changes the choice, and any other pinch drops it. The panel says whose turn
    // it is (and check, mate or a draw), what each side has lost, and takes a move back, starts over or
    // lifts the board to put it somewhere else. A pawn that reaches the far rank becomes a queen.
    // Hands are off: nothing here uses the hand's position, so there's nothing to lose when it leaves
    // the camera's view. Logs [Chess].
    public class ChessDemo : Demo, IPinchHandler
    {
        [SerializeField] GameObject m_BoardPrefab;

        CoreServices m_Core;
        ChessBoard m_Board;
        ChessPosition m_Position;
        readonly List<Move> m_Moves = new();
        readonly List<int> m_Targets = new();
        int m_Selected = -1;
        Outcome m_Outcome;

        RectTransform m_Panel;
        TMP_Text m_Status;
        TMP_Text m_Lost;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            core.Placer.Placed += OnPlaced;
            WaitForBoard();
            BuildPanel();
            UpdatePanel();
            Debug.Log("[Chess] Ready: look at a table or the floor and pinch to put the board there");
        }

        // DemoManager takes down the placed board and puts back the placer's prefab and the pinch
        // handler; what's left is the event.
        public override void OnDemoExit(CoreServices core)
        {
            core.Placer.Placed -= OnPlaced;
            if (ReferenceEquals(PinchRouter.Handler, this))
                PinchRouter.Clear();
            Debug.Log($"[Chess] Leaving after {m_Moves.Count} moves");
        }

        // No board yet: a pinch on a surface places one, and nothing here takes pinches until then.
        void WaitForBoard()
        {
            m_Board = null;
            m_Selected = -1;
            m_Targets.Clear();
            m_Core.Placer.ObjectPrefab = m_BoardPrefab;
            if (ReferenceEquals(PinchRouter.Handler, this))
                PinchRouter.Clear();
        }

        void OnPlaced(GameObject placed)
        {
            if (!placed.TryGetComponent<ChessBoard>(out var board))
                return;
            // Standing on the surface: its origin is the middle of the board on it, not of its height.
            placed.transform.localPosition = Vector3.zero;
            m_Board = board;
            m_Core.Placer.ObjectPrefab = null;
            PinchRouter.Handler = this;
            // A board lifted and put down again keeps its game; a first board starts one.
            if (m_Position == null)
                NewGame(log: false);
            Refresh();
            var distance = Vector3.Distance(m_Core.Head.position, placed.transform.position);
            Debug.Log($"[Chess] Board placed {distance:F1} m away");
        }

        // A pinch began with the gaze on gazeTarget. The board's squares and pieces are what count:
        // the gaze finds them without the hand, so a camera pinch does as well as a hand one.
        public bool TryBeginPinch(GameObject gazeTarget, PinchSource source)
        {
            if (m_Board == null || m_Outcome != Outcome.Playing)
                return false;
            var square = m_Board.SquareOf(gazeTarget);
            if (square < 0)
            {
                // Somewhere off the board: drop the choice, and take the pinch so it doesn't click.
                if (m_Selected < 0)
                    return false;
                Select(-1);
                return true;
            }
            Touch(square);
            return true;
        }

        // The pinch is a click, over as it starts: nothing is held, so there's nothing to end.
        public void EndPinch()
        {
        }

        // What a pinch on a square does, for the side to move.
        void Touch(int square)
        {
            if (m_Selected >= 0 && m_Targets.Contains(square))
            {
                MovePiece(m_Selected, square);
                return;
            }
            var piece = m_Position[square];
            if (!piece.IsNone && piece.Side == m_Position.Turn && square != m_Selected)
                Select(square);
            else
                Select(-1);
        }

        void Select(int square)
        {
            m_Selected = square;
            m_Targets.Clear();
            if (square >= 0)
                m_Targets.AddRange(m_Position.TargetsFrom(square));
            if (square >= 0)
                Debug.Log($"[Chess] Chose {m_Position[square].Kind} on {ChessRules.Name(square)}: {m_Targets.Count} squares to go to");
            // A piece that can't go anywhere is still chosen, so the player sees that it's stuck.
            RefreshMarks();
        }

        void MovePiece(int from, int to)
        {
            if (!m_Position.TryFind(from, to, out var move))
                return;
            var mover = m_Position[from];
            var side = m_Position.Turn;
            var number = m_Position.FullmoveNumber;
            m_Position.Play(move);
            m_Moves.Add(move);
            m_Selected = -1;
            m_Targets.Clear();
            m_Outcome = m_Position.Status();
            var taken = m_Position.LastMoveCaptured ? " takes" : "";
            Debug.Log($"[Chess] {number}. {ChessRules.SideName(side)} {mover.Kind} {move}{taken}");
            if (m_Outcome != Outcome.Playing)
                Debug.Log($"[Chess] {ChessRules.Describe(m_Outcome, m_Position.Turn, false)} After {m_Moves.Count} moves");
            else if (m_Position.InCheck(m_Position.Turn))
                Debug.Log($"[Chess] Check on {ChessRules.SideName(m_Position.Turn)}");
            Refresh();
        }

        void NewGame(bool log = true)
        {
            m_Position = ChessPosition.Start();
            m_Moves.Clear();
            m_Selected = -1;
            m_Targets.Clear();
            m_Outcome = Outcome.Playing;
            if (log)
                Debug.Log("[Chess] New game");
            if (m_Board != null)
                Refresh();
        }

        // Takes the last move back by playing the game again without it.
        void Undo()
        {
            if (m_Moves.Count == 0)
                return;
            m_Moves.RemoveAt(m_Moves.Count - 1);
            m_Position = ChessRules.Replay(m_Moves, m_Moves.Count);
            m_Outcome = m_Position.Status();
            m_Selected = -1;
            m_Targets.Clear();
            Debug.Log($"[Chess] Took a move back: {m_Moves.Count} moves");
            Refresh();
        }

        // Lifts the board so it can be put somewhere else. The game stays as it is.
        void MoveBoard()
        {
            if (m_Board == null)
                return;
            Debug.Log("[Chess] Lifting the board to move it");
            m_Core.Placer.RemoveAll();
            WaitForBoard();
            UpdatePanel();
        }

        void Refresh()
        {
            if (m_Board != null)
                m_Board.Show(m_Position);
            RefreshMarks();
            UpdatePanel();
        }

        void RefreshMarks()
        {
            if (m_Board != null)
                m_Board.Mark(m_Selected, m_Targets, CheckedKing());
            UpdatePanel();
        }

        // The square of the king in check, if the side to move is in check, or -1.
        int CheckedKing()
        {
            if (m_Position == null || !m_Position.InCheck(m_Position.Turn))
                return -1;
            for (var i = 0; i < 64; i++)
            {
                var piece = m_Position[i];
                if (piece.Kind == PieceKind.King && piece.Side == m_Position.Turn)
                    return i;
            }
            return -1;
        }

        void UpdatePanel()
        {
            if (m_Status == null)
                return;
            if (m_Board == null)
            {
                m_Status.text = "Look at a table or the floor and pinch to put the board there.\nNo highlight yet? Look at the surface for a moment.";
                m_Lost.text = m_Position == null ? "" : "Your game is kept; it comes back on the new spot.";
                return;
            }
            m_Status.text = ChessRules.Describe(m_Outcome, m_Position.Turn, m_Position.InCheck(m_Position.Turn));
            m_Lost.text = $"White lost: {ChessRules.LostText(ChessRules.Lost(m_Position, Side.White))}\n" +
                          $"Black lost: {ChessRules.LostText(ChessRules.Lost(m_Position, Side.Black))}";
        }

        // A small Material panel ahead and a little above the eyes, clear of the table where the board
        // goes: whose turn it is, what's been lost, and three buttons. The lobby is on the settings panel.
        void BuildPanel()
        {
            const float distance = 1.5f;
            m_Panel = WorldUi.Panel("Chess Panel", transform, new Vector2(440f, 280f), distance);
            m_Status = M3.Text("Status", m_Panel, "", M3Theme.Type.BodyLarge, new Vector2(0f, 92f), new Vector2(392f, 64f));
            m_Lost = M3.Text("Lost", m_Panel, "", M3Theme.Type.BodyMedium, new Vector2(0f, 28f), new Vector2(392f, 56f));
            M3.Button("Undo", m_Panel, "Undo", M3Button.Variant.Tonal, new Vector2(-70f, -32f), Undo, "undo");
            M3.Button("New game", m_Panel, "New", M3Button.Variant.Tonal, new Vector2(70f, -32f), () => NewGame(), "restart_alt");
            M3.Button("Move board", m_Panel, "Move board", M3Button.Variant.Text, new Vector2(0f, -92f), MoveBoard, "open_with");
            WorldUi.PlaceInFront(m_Panel, m_Core.Head, distance, 12f, -25f);
        }
    }
}
