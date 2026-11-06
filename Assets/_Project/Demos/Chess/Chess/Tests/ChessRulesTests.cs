using System.Collections.Generic;
using CardboardXR.Demos.Chess;
using NUnit.Framework;

namespace CardboardXR.Demos.Chess.Tests
{
    public class ChessRulesTests
    {
        // "e4" as a square number.
        static int Sq(string name) => ChessPosition.Square(name[0] - 'a', name[1] - '1');

        static Move Mv(string from, string to, PieceKind promotion = PieceKind.None) => new(Sq(from), Sq(to), promotion);

        static void Play(ChessPosition position, params string[] moves)
        {
            foreach (var text in moves)
                Assert.IsTrue(position.Play(Mv(text.Substring(0, 2), text.Substring(2, 2))), $"{text} should be legal");
        }

        // A position from the first four fields of a FEN string: pieces, side to move, castling, en passant.
        static ChessPosition Fen(string fen)
        {
            var parts = fen.Split(' ');
            var position = ChessPosition.Empty();
            var rows = parts[0].Split('/');
            for (var i = 0; i < 8; i++)
            {
                var rank = 7 - i;
                var file = 0;
                foreach (var ch in rows[i])
                {
                    if (char.IsDigit(ch))
                    {
                        file += ch - '0';
                        continue;
                    }
                    var kind = char.ToLowerInvariant(ch) switch
                    {
                        'p' => PieceKind.Pawn,
                        'n' => PieceKind.Knight,
                        'b' => PieceKind.Bishop,
                        'r' => PieceKind.Rook,
                        'q' => PieceKind.Queen,
                        _ => PieceKind.King,
                    };
                    position.Set(ChessPosition.Square(file, rank), kind, char.IsUpper(ch) ? Side.White : Side.Black);
                    file++;
                }
            }
            var castling = parts[2];
            var enPassant = parts[3] == "-" ? -1 : Sq(parts[3]);
            position.Begin(parts[1] == "w" ? Side.White : Side.Black,
                castling.Contains("K"), castling.Contains("Q"), castling.Contains("k"), castling.Contains("q"), enPassant);
            return position;
        }

        // The number of games of this many moves from a position: the standard check that every
        // kind of move is made, and no others.
        static long Perft(ChessPosition position, int depth)
        {
            var moves = position.LegalMoves();
            if (depth == 1)
                return moves.Count;
            long total = 0;
            foreach (var move in moves)
            {
                var next = position.Clone();
                Assert.IsTrue(next.Play(move));
                total += Perft(next, depth - 1);
            }
            return total;
        }

        [Test]
        public void StartingPositionHasTwentyMovesAndTheKnownCounts()
        {
            var position = ChessPosition.Start();
            Assert.AreEqual(20, Perft(position, 1));
            Assert.AreEqual(400, Perft(position, 2));
            Assert.AreEqual(8902, Perft(position, 3));
        }

        [Test]
        public void MoveCountsMatchTheKnownTestPositions()
        {
            // Castling, en passant, pins and checks all at once.
            var kiwipete = Fen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -");
            Assert.AreEqual(48, Perft(kiwipete, 1));
            Assert.AreEqual(2039, Perft(kiwipete, 2));
            // En passant that would uncover a check, and many checks.
            var endgame = Fen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -");
            Assert.AreEqual(14, Perft(endgame, 1));
            Assert.AreEqual(191, Perft(endgame, 2));
            Assert.AreEqual(2812, Perft(endgame, 3));
            // Promotions, under checks, and castling rights lost by capture.
            var promotions = Fen("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq -");
            Assert.AreEqual(6, Perft(promotions, 1));
            Assert.AreEqual(264, Perft(promotions, 2));
            Assert.AreEqual(9467, Perft(promotions, 3));
        }

        [Test]
        public void SquaresAreNamedFromA1()
        {
            Assert.AreEqual("a1", ChessPosition.Name(0));
            Assert.AreEqual("h1", ChessPosition.Name(7));
            Assert.AreEqual("e4", ChessPosition.Name(28));
            Assert.AreEqual("h8", ChessPosition.Name(63));
            Assert.AreEqual("e2e4", Mv("e2", "e4").ToString());
            Assert.AreEqual("a7a8q", Mv("a7", "a8", PieceKind.Queen).ToString());
        }

        [Test]
        public void FoolsMateIsCheckmateForBlack()
        {
            var position = ChessPosition.Start();
            Play(position, "f2f3", "e7e5", "g2g4");
            Assert.AreEqual(Outcome.Playing, position.Status());
            Play(position, "d8h4");
            Assert.IsTrue(position.InCheck(Side.White));
            Assert.AreEqual(Outcome.Checkmate, position.Status());
            Assert.AreEqual("Checkmate. Black wins.", ChessRules.Describe(Outcome.Checkmate, position.Turn, true));
        }

        [Test]
        public void ScholarsMateIsCheckmateForWhite()
        {
            var position = ChessPosition.Start();
            Play(position, "e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6", "h5f7");
            Assert.AreEqual(Outcome.Checkmate, position.Status());
            Assert.AreEqual(Side.Black, position.Turn);
        }

        [Test]
        public void ANoMoveSideNotInCheckIsStalemate()
        {
            var position = Fen("7k/5Q2/6K1/8/8/8/8/8 b - -");
            Assert.IsFalse(position.InCheck(Side.Black));
            Assert.AreEqual(Outcome.Stalemate, position.Status());
        }

        [Test]
        public void ThePositionThreeTimesIsADraw()
        {
            var position = ChessPosition.Start();
            Play(position, "g1f3", "g8f6", "f3g1", "f6g8");
            Assert.AreEqual(Outcome.Playing, position.Status());
            Play(position, "g1f3", "g8f6", "f3g1", "f6g8");
            Assert.AreEqual(Outcome.Repetition, position.Status());
        }

        [Test]
        public void KingsAloneOrWithOneMinorPieceCantMate()
        {
            Assert.AreEqual(Outcome.DeadPosition, Fen("4k3/8/8/8/8/8/8/4K3 w - -").Status());
            Assert.AreEqual(Outcome.DeadPosition, Fen("4k3/8/8/8/8/8/8/3BK3 w - -").Status());
            Assert.AreEqual(Outcome.Playing, Fen("4k3/8/8/8/8/8/8/3RK3 w - -").Status());
            Assert.AreEqual(Outcome.Playing, Fen("4k3/8/8/8/8/8/P7/4K3 w - -").Status());
        }

        [Test]
        public void CastlingNeedsEmptySquaresAndNoCheck()
        {
            var open = Fen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq -");
            Assert.IsTrue(open.TryFind(Sq("e1"), Sq("g1"), out _));
            Assert.IsTrue(open.TryFind(Sq("e1"), Sq("c1"), out _));

            // The king would cross an attacked square.
            var crossing = Fen("5r1k/8/8/8/8/8/8/4K2R w K -");
            Assert.IsFalse(crossing.TryFind(Sq("e1"), Sq("g1"), out _));

            // The king is in check.
            var inCheck = Fen("4r2k/8/8/8/8/8/8/R3K2R w KQ -");
            Assert.IsFalse(inCheck.TryFind(Sq("e1"), Sq("g1"), out _));
            Assert.IsFalse(inCheck.TryFind(Sq("e1"), Sq("c1"), out _));

            // No right left: the king moved before.
            var moved = Fen("r3k2r/8/8/8/8/8/8/R3K2R w kq -");
            Assert.IsFalse(moved.TryFind(Sq("e1"), Sq("g1"), out _));
        }

        [Test]
        public void CastlingMovesTheRookToo()
        {
            var position = Fen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq -");
            Play(position, "e1g1");
            Assert.AreEqual(PieceKind.King, position[Sq("g1")].Kind);
            Assert.AreEqual(PieceKind.Rook, position[Sq("f1")].Kind);
            Assert.IsTrue(position[Sq("h1")].IsNone);
            Play(position, "e8c8");
            Assert.AreEqual(PieceKind.Rook, position[Sq("d8")].Kind);
            Assert.IsTrue(position[Sq("a8")].IsNone);
        }

        [Test]
        public void EnPassantTakesThePawnBesideIt()
        {
            var position = Fen("4k3/8/8/3pP3/8/8/8/4K3 w - d6");
            Assert.IsTrue(position.TryFind(Sq("e5"), Sq("d6"), out _));
            Play(position, "e5d6");
            Assert.IsTrue(position[Sq("d5")].IsNone, "the pawn that moved two is taken");
            Assert.AreEqual(PieceKind.Pawn, position[Sq("d6")].Kind);
            Assert.IsTrue(position.LastMoveCaptured);
        }

        [Test]
        public void EnPassantIsOnlyOfferedForOneMove()
        {
            var position = ChessPosition.Start();
            Play(position, "e2e4", "a7a6", "e4e5", "d7d5");
            Assert.AreEqual(Sq("d6"), position.EnPassantSquare);
            Assert.IsTrue(position.TryFind(Sq("e5"), Sq("d6"), out _));
            Play(position, "h2h3", "h7h6");
            Assert.IsFalse(position.TryFind(Sq("e5"), Sq("d6"), out _));
        }

        [Test]
        public void APawnOnTheLastRankBecomesAQueenUnlessTold()
        {
            var position = Fen("8/P6k/8/8/8/8/8/K7 w - -");
            Assert.AreEqual(4, position.LegalMoves().FindAll(move => move.From == Sq("a7")).Count);
            Assert.IsTrue(position.TryFind(Sq("a7"), Sq("a8"), out var found));
            Assert.AreEqual(PieceKind.Queen, found.Promotion);
            Assert.AreEqual(new List<int> { Sq("a8") }, position.TargetsFrom(Sq("a7")));

            var rook = position.Clone();
            Assert.IsTrue(rook.Play(Mv("a7", "a8", PieceKind.Rook)));
            Assert.AreEqual(PieceKind.Rook, rook[Sq("a8")].Kind);

            Assert.IsTrue(position.Play(Mv("a7", "a8")));
            Assert.AreEqual(PieceKind.Queen, position[Sq("a8")].Kind);
        }

        [Test]
        public void APinnedPieceCantMoveOffItsLine()
        {
            var position = Fen("4r2k/8/8/8/8/8/4B3/4K3 w - -");
            Assert.IsEmpty(position.TargetsFrom(Sq("e2")));
            Assert.IsFalse(position.TryFind(Sq("e2"), Sq("d3"), out _));
        }

        [Test]
        public void ThePieceOfTheSideNotToMoveHasNoMoves()
        {
            var position = ChessPosition.Start();
            Assert.IsEmpty(position.TargetsFrom(Sq("e7")));
            Assert.IsFalse(position.Play(Mv("e7", "e5")));
            Assert.IsFalse(position.Play(Mv("e3", "e4")), "no piece there");
            Assert.AreEqual(Side.White, position.Turn);
        }

        [Test]
        public void TheKingCantStepIntoCheck()
        {
            var position = Fen("4k3/8/8/8/8/8/3r4/4K3 w - -");
            var targets = position.TargetsFrom(Sq("e1"));
            Assert.IsFalse(targets.Contains(Sq("e2")), "the rook guards e2");
            Assert.IsTrue(targets.Contains(Sq("d2")), "the rook can be taken");
        }

        [Test]
        public void PlayingTheMovesAgainWithoutTheLastTakesItBack()
        {
            var moves = new List<Move> { Mv("e2", "e4"), Mv("e7", "e5"), Mv("g1", "f3") };
            var full = ChessRules.Replay(moves, 3);
            Assert.AreEqual(Side.Black, full.Turn);
            Assert.AreEqual(PieceKind.Knight, full[Sq("f3")].Kind);

            var back = ChessRules.Replay(moves, 2);
            Assert.AreEqual(Side.White, back.Turn);
            Assert.IsTrue(back[Sq("f3")].IsNone);
            Assert.AreEqual(PieceKind.Knight, back[Sq("g1")].Kind);
            Assert.AreEqual(PieceKind.Pawn, back[Sq("e5")].Kind);

            var start = ChessRules.Replay(moves, 0);
            Assert.AreEqual(20, start.LegalMoves().Count);
        }

        [Test]
        public void LostPiecesAreCountedFromTheFullSet()
        {
            var position = ChessPosition.Start();
            Assert.AreEqual("nothing", ChessRules.LostText(ChessRules.Lost(position, Side.White)));
            Play(position, "e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a5");
            Assert.AreEqual("pawn", ChessRules.LostText(ChessRules.Lost(position, Side.White)));
            Assert.AreEqual("pawn", ChessRules.LostText(ChessRules.Lost(position, Side.Black)));
            Play(position, "c3b5", "a5b5");
            Assert.AreEqual("pawn, knight", ChessRules.LostText(ChessRules.Lost(position, Side.White)));
            Assert.AreEqual("2 pawns", ChessRules.LostText(new[] { 2, 0, 0, 0, 0, 0 }));
        }

        [Test]
        public void EveryBoardSquareIsFoundAgainAtItsCenter()
        {
            const float size = ChessBoard.k_SquareSize;
            for (var square = 0; square < 64; square++)
            {
                var (x, z) = ChessRules.SquareCenter(square, size);
                Assert.AreEqual(square, ChessRules.SquareAt(x, z, size), ChessPosition.Name(square));
            }
            Assert.AreEqual(-1, ChessRules.SquareAt(5f * size, 0f, size));
            Assert.AreEqual(-1, ChessRules.SquareAt(0f, -4.5f * size, size));
        }

        [Test]
        public void WhiteStandsOnTheNearSideAndTheFilesRunLeftToRight()
        {
            const float size = ChessBoard.k_SquareSize;
            var (a1x, a1z) = ChessRules.SquareCenter(Sq("a1"), size);
            var (h8x, h8z) = ChessRules.SquareCenter(Sq("h8"), size);
            // Seen from white's side (looking along -z), a is to the left of h, and rank 1 is nearer.
            Assert.Less(a1x, h8x);
            Assert.Greater(a1z, h8z);
        }
    }
}
