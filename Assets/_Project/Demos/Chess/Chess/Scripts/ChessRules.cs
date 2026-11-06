using System.Collections.Generic;

namespace CardboardXR.Demos.Chess
{
    public enum Side
    {
        White,
        Black,
    }

    public enum PieceKind
    {
        None,
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King,
    }

    public enum Outcome
    {
        Playing,
        Checkmate,
        Stalemate,
        FiftyMoves,
        Repetition,
        DeadPosition,
    }

    // One piece on the board.
    public readonly struct Piece
    {
        public readonly PieceKind Kind;
        public readonly Side Side;

        public Piece(PieceKind kind, Side side)
        {
            Kind = kind;
            Side = side;
        }

        public bool IsNone => Kind == PieceKind.None;
    }

    // A move between two squares. Squares are 0-63, from a1 (0) along the first rank to h1 (7), then
    // up: e2 is 12 and e4 is 28. Promotion is the piece a pawn becomes, or None.
    public readonly struct Move
    {
        public readonly int From;
        public readonly int To;
        public readonly PieceKind Promotion;

        public Move(int from, int to, PieceKind promotion = PieceKind.None)
        {
            From = from;
            To = to;
            Promotion = promotion;
        }

        public override string ToString() =>
            ChessRules.Name(From) + ChessRules.Name(To) + (Promotion == PieceKind.None ? "" : Letter(Promotion).ToString().ToLowerInvariant());

        static char Letter(PieceKind kind) => kind switch
        {
            PieceKind.Knight => 'N',
            PieceKind.Bishop => 'B',
            PieceKind.Rook => 'R',
            PieceKind.Queen => 'Q',
            _ => '?',
        };
    }

    // A game of chess, apart from anything on screen: the position, the legal moves, and how a game
    // ends. Pure, so the demo only has to draw it (see ChessRulesTests). Squares are 0-63 as in Move.
    public class ChessPosition
    {
        readonly Piece[] m_Squares = new Piece[64];
        // The square a pawn that just moved two could be taken on, or -1.
        int m_EnPassant = -1;
        // Castling rights: white king side, white queen side, black king side, black queen side.
        readonly bool[] m_Castle = new bool[4];
        int m_HalfmoveClock;
        int m_FullmoveNumber = 1;
        readonly Dictionary<string, int> m_Seen = new();

        public Side Turn { get; private set; } = Side.White;
        public int FullmoveNumber => m_FullmoveNumber;
        public int HalfmoveClock => m_HalfmoveClock;
        public int EnPassantSquare => m_EnPassant;

        public Piece this[int square] => m_Squares[square];

        public static ChessPosition Start()
        {
            var position = new ChessPosition();
            const PieceKind R = PieceKind.Rook, N = PieceKind.Knight, B = PieceKind.Bishop, Q = PieceKind.Queen, K = PieceKind.King;
            var back = new[] { R, N, B, Q, K, B, N, R };
            for (var file = 0; file < 8; file++)
            {
                position.m_Squares[file] = new Piece(back[file], Side.White);
                position.m_Squares[8 + file] = new Piece(PieceKind.Pawn, Side.White);
                position.m_Squares[48 + file] = new Piece(PieceKind.Pawn, Side.Black);
                position.m_Squares[56 + file] = new Piece(back[file], Side.Black);
            }
            for (var i = 0; i < 4; i++)
                position.m_Castle[i] = true;
            position.Record();
            return position;
        }

        // An empty board, for setting up a position in a test: Set pieces, then Begin.
        public static ChessPosition Empty() => new();

        public void Set(int square, PieceKind kind, Side side) => m_Squares[square] = new Piece(kind, side);

        // Makes the position as set up the start of a game, with this side to move and these castling
        // rights (on the same order as the fields above) and en passant square (or -1).
        public void Begin(Side turn, bool whiteKingSide = false, bool whiteQueenSide = false,
            bool blackKingSide = false, bool blackQueenSide = false, int enPassant = -1)
        {
            Turn = turn;
            m_Castle[0] = whiteKingSide;
            m_Castle[1] = whiteQueenSide;
            m_Castle[2] = blackKingSide;
            m_Castle[3] = blackQueenSide;
            m_EnPassant = enPassant;
            m_Seen.Clear();
            Record();
        }

        public static int Square(int file, int rank) => rank * 8 + file;
        public static int FileOf(int square) => square & 7;
        public static int RankOf(int square) => square >> 3;

        // "e4" for 28.
        public static string Name(int square) => $"{(char)('a' + FileOf(square))}{RankOf(square) + 1}";

        public bool InCheck(Side side) => InCheck(m_Squares, side);

        // Every move the side to move may make. A pawn reaching the last rank gets one move per piece
        // it can become.
        public List<Move> LegalMoves()
        {
            var moves = new List<Move>(40);
            for (var from = 0; from < 64; from++)
            {
                if (!m_Squares[from].IsNone && m_Squares[from].Side == Turn)
                    AddLegalMoves(from, moves);
            }
            return moves;
        }

        // The squares the piece on from may move to: for lighting them up. A promotion's four moves
        // share one square, which is listed once.
        public List<int> TargetsFrom(int from)
        {
            var targets = new List<int>();
            if (m_Squares[from].IsNone || m_Squares[from].Side != Turn)
                return targets;
            var moves = new List<Move>(28);
            AddLegalMoves(from, moves);
            foreach (var move in moves)
            {
                if (!targets.Contains(move.To))
                    targets.Add(move.To);
            }
            return targets;
        }

        // Whether the side to move has this move, and the move as the position knows it (promotion
        // filled in, always a queen when the player gave none).
        public bool TryFind(int from, int to, out Move found)
        {
            found = default;
            var moves = new List<Move>(28);
            if (m_Squares[from].IsNone || m_Squares[from].Side != Turn)
                return false;
            AddLegalMoves(from, moves);
            foreach (var move in moves)
            {
                if (move.To != to)
                    continue;
                if (move.Promotion != PieceKind.None && move.Promotion != PieceKind.Queen)
                    continue;
                found = move;
                return true;
            }
            return false;
        }

        // Plays a move the side to move has: true if it did. A pawn move to the last rank with no
        // promotion given becomes a queen, as TryFind does. The demo reads what stood on the squares
        // before it plays.
        public bool Play(Move move)
        {
            if (m_Squares[move.From].IsNone || m_Squares[move.From].Side != Turn)
                return false;
            var moves = new List<Move>(28);
            AddLegalMoves(move.From, moves);
            var found = false;
            var legal = default(Move);
            foreach (var candidate in moves)
            {
                if (candidate.To != move.To)
                    continue;
                if (candidate.Promotion == move.Promotion || move.Promotion == PieceKind.None && candidate.Promotion == PieceKind.Queen)
                {
                    legal = candidate;
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
            var wasPawn = m_Squares[legal.From].Kind == PieceKind.Pawn;
            Apply(m_Squares, legal, ref m_EnPassant, m_Castle, out var captured);
            m_CapturedLast = captured;
            m_HalfmoveClock = wasPawn || captured ? 0 : m_HalfmoveClock + 1;
            if (Turn == Side.Black)
                m_FullmoveNumber++;
            Turn = Turn == Side.White ? Side.Black : Side.White;
            Record();
            return true;
        }

        // A copy that can be played on without touching this one: for looking ahead, and for tests.
        public ChessPosition Clone()
        {
            var copy = new ChessPosition();
            System.Array.Copy(m_Squares, copy.m_Squares, 64);
            System.Array.Copy(m_Castle, copy.m_Castle, 4);
            copy.m_EnPassant = m_EnPassant;
            copy.m_HalfmoveClock = m_HalfmoveClock;
            copy.m_FullmoveNumber = m_FullmoveNumber;
            copy.Turn = Turn;
            foreach (var entry in m_Seen)
                copy.m_Seen[entry.Key] = entry.Value;
            return copy;
        }

        // How the game stands, for the side to move.
        public Outcome Status()
        {
            if (LegalMoves().Count == 0)
                return InCheck(Turn) ? Outcome.Checkmate : Outcome.Stalemate;
            if (m_Seen.TryGetValue(Key(), out var times) && times >= 3)
                return Outcome.Repetition;
            if (m_HalfmoveClock >= 100)
                return Outcome.FiftyMoves;
            if (IsDeadPosition())
                return Outcome.DeadPosition;
            return Outcome.Playing;
        }

        // No mate is possible: kings alone, or a king and one minor piece against a king.
        bool IsDeadPosition()
        {
            var minors = 0;
            for (var i = 0; i < 64; i++)
            {
                var kind = m_Squares[i].Kind;
                if (kind is PieceKind.None or PieceKind.King)
                    continue;
                if (kind is PieceKind.Knight or PieceKind.Bishop)
                    minors++;
                else
                    return false;
            }
            return minors <= 1;
        }

        // ---- Moves ----

        bool m_CapturedLast;

        static readonly int[] s_KnightFiles = { 1, 2, 2, 1, -1, -2, -2, -1 };
        static readonly int[] s_KnightRanks = { 2, 1, -1, -2, -2, -1, 1, 2 };
        static readonly int[] s_KingFiles = { 1, 1, 1, 0, -1, -1, -1, 0 };
        static readonly int[] s_KingRanks = { 1, 0, -1, -1, -1, 0, 1, 1 };
        static readonly int[] s_DiagonalFiles = { 1, 1, -1, -1 };
        static readonly int[] s_DiagonalRanks = { 1, -1, 1, -1 };
        static readonly int[] s_StraightFiles = { 1, -1, 0, 0 };
        static readonly int[] s_StraightRanks = { 0, 0, 1, -1 };

        void AddLegalMoves(int from, List<Move> moves)
        {
            var side = m_Squares[from].Side;
            var pseudo = new List<Move>(28);
            AddPseudoMoves(m_Squares, from, m_EnPassant, m_Castle, pseudo);
            foreach (var move in pseudo)
            {
                // A move is legal if it doesn't leave the mover's own king in check.
                var copy = (Piece[])m_Squares.Clone();
                var enPassant = m_EnPassant;
                var castle = (bool[])m_Castle.Clone();
                Apply(copy, move, ref enPassant, castle);
                if (!InCheck(copy, side))
                    moves.Add(move);
            }
        }

        static void AddPseudoMoves(Piece[] board, int from, int enPassant, bool[] castle, List<Move> moves)
        {
            var piece = board[from];
            var file = FileOf(from);
            var rank = RankOf(from);
            switch (piece.Kind)
            {
                case PieceKind.Pawn:
                    AddPawnMoves(board, from, piece.Side, enPassant, moves);
                    break;
                case PieceKind.Knight:
                    for (var i = 0; i < 8; i++)
                        AddStep(board, from, piece.Side, file + s_KnightFiles[i], rank + s_KnightRanks[i], moves);
                    break;
                case PieceKind.Bishop:
                    AddSlides(board, from, piece.Side, s_DiagonalFiles, s_DiagonalRanks, moves);
                    break;
                case PieceKind.Rook:
                    AddSlides(board, from, piece.Side, s_StraightFiles, s_StraightRanks, moves);
                    break;
                case PieceKind.Queen:
                    AddSlides(board, from, piece.Side, s_DiagonalFiles, s_DiagonalRanks, moves);
                    AddSlides(board, from, piece.Side, s_StraightFiles, s_StraightRanks, moves);
                    break;
                case PieceKind.King:
                    for (var i = 0; i < 8; i++)
                        AddStep(board, from, piece.Side, file + s_KingFiles[i], rank + s_KingRanks[i], moves);
                    AddCastling(board, from, piece.Side, castle, moves);
                    break;
            }
        }

        static void AddStep(Piece[] board, int from, Side side, int file, int rank, List<Move> moves)
        {
            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return;
            var to = Square(file, rank);
            if (board[to].IsNone || board[to].Side != side)
                moves.Add(new Move(from, to));
        }

        static void AddSlides(Piece[] board, int from, Side side, int[] files, int[] ranks, List<Move> moves)
        {
            for (var i = 0; i < files.Length; i++)
            {
                var file = FileOf(from) + files[i];
                var rank = RankOf(from) + ranks[i];
                while (file >= 0 && file <= 7 && rank >= 0 && rank <= 7)
                {
                    var to = Square(file, rank);
                    if (board[to].IsNone)
                    {
                        moves.Add(new Move(from, to));
                    }
                    else
                    {
                        if (board[to].Side != side)
                            moves.Add(new Move(from, to));
                        break;
                    }
                    file += files[i];
                    rank += ranks[i];
                }
            }
        }

        static void AddPawnMoves(Piece[] board, int from, Side side, int enPassant, List<Move> moves)
        {
            var direction = side == Side.White ? 1 : -1;
            var startRank = side == Side.White ? 1 : 6;
            var lastRank = side == Side.White ? 7 : 0;
            var file = FileOf(from);
            var rank = RankOf(from);
            var ahead = rank + direction;
            if (ahead < 0 || ahead > 7)
                return;
            if (board[Square(file, ahead)].IsNone)
            {
                AddPawnMove(from, Square(file, ahead), ahead == lastRank, moves);
                if (rank == startRank && board[Square(file, ahead + direction)].IsNone)
                    moves.Add(new Move(from, Square(file, ahead + direction)));
            }
            for (var side2 = -1; side2 <= 1; side2 += 2)
            {
                var targetFile = file + side2;
                if (targetFile < 0 || targetFile > 7)
                    continue;
                var to = Square(targetFile, ahead);
                if (!board[to].IsNone && board[to].Side != side)
                    AddPawnMove(from, to, ahead == lastRank, moves);
                else if (to == enPassant)
                    moves.Add(new Move(from, to));
            }
        }

        static void AddPawnMove(int from, int to, bool promotes, List<Move> moves)
        {
            if (!promotes)
            {
                moves.Add(new Move(from, to));
                return;
            }
            moves.Add(new Move(from, to, PieceKind.Queen));
            moves.Add(new Move(from, to, PieceKind.Rook));
            moves.Add(new Move(from, to, PieceKind.Bishop));
            moves.Add(new Move(from, to, PieceKind.Knight));
        }

        // Castling: the king and that rook haven't moved, the squares between are empty, and the king
        // isn't in check, doesn't cross an attacked square and doesn't land on one.
        static void AddCastling(Piece[] board, int from, Side side, bool[] castle, List<Move> moves)
        {
            var rank = side == Side.White ? 0 : 7;
            var home = Square(4, rank);
            if (from != home || InCheck(board, side))
                return;
            var offset = side == Side.White ? 0 : 2;
            if (castle[offset] && board[Square(7, rank)].Kind == PieceKind.Rook && board[Square(7, rank)].Side == side
                && board[Square(5, rank)].IsNone && board[Square(6, rank)].IsNone
                && !Attacked(board, Square(5, rank), Other(side)) && !Attacked(board, Square(6, rank), Other(side)))
                moves.Add(new Move(home, Square(6, rank)));
            if (castle[offset + 1] && board[Square(0, rank)].Kind == PieceKind.Rook && board[Square(0, rank)].Side == side
                && board[Square(1, rank)].IsNone && board[Square(2, rank)].IsNone && board[Square(3, rank)].IsNone
                && !Attacked(board, Square(3, rank), Other(side)) && !Attacked(board, Square(2, rank), Other(side)))
                moves.Add(new Move(home, Square(2, rank)));
        }

        static Side Other(Side side) => side == Side.White ? Side.Black : Side.White;

        // Makes a move on a board, with the side effects: a pawn taken en passant, the rook that
        // castles, a pawn that promotes, the en passant square the move leaves and the castling rights
        // it takes away.
        static void Apply(Piece[] board, Move move, ref int enPassant, bool[] castle) =>
            Apply(board, move, ref enPassant, castle, out _);

        static void Apply(Piece[] board, Move move, ref int enPassant, bool[] castle, out bool captured)
        {
            var piece = board[move.From];
            captured = !board[move.To].IsNone;
            var newEnPassant = -1;
            if (piece.Kind == PieceKind.Pawn)
            {
                if (move.To == enPassant && FileOf(move.From) != FileOf(move.To) && board[move.To].IsNone)
                {
                    // The pawn taken stands beside the mover, on the mover's own rank.
                    board[Square(FileOf(move.To), RankOf(move.From))] = default;
                    captured = true;
                }
                if (System.Math.Abs(RankOf(move.To) - RankOf(move.From)) == 2)
                    newEnPassant = (move.From + move.To) / 2;
            }
            if (piece.Kind == PieceKind.King && System.Math.Abs(FileOf(move.To) - FileOf(move.From)) == 2)
            {
                var rank = RankOf(move.From);
                var kingSide = FileOf(move.To) == 6;
                var rookFrom = Square(kingSide ? 7 : 0, rank);
                var rookTo = Square(kingSide ? 5 : 3, rank);
                board[rookTo] = board[rookFrom];
                board[rookFrom] = default;
            }
            board[move.To] = move.Promotion == PieceKind.None ? piece : new Piece(move.Promotion, piece.Side);
            board[move.From] = default;
            enPassant = newEnPassant;
            // Moving the king or a rook from home, or taking a rook at home, ends that castling.
            if (piece.Kind == PieceKind.King)
            {
                var offset = piece.Side == Side.White ? 0 : 2;
                castle[offset] = false;
                castle[offset + 1] = false;
            }
            ClearRookRight(castle, move.From);
            ClearRookRight(castle, move.To);
        }

        static void ClearRookRight(bool[] castle, int square)
        {
            switch (square)
            {
                case 7: castle[0] = false; break;
                case 0: castle[1] = false; break;
                case 63: castle[2] = false; break;
                case 56: castle[3] = false; break;
            }
        }

        // ---- Check ----

        static bool InCheck(Piece[] board, Side side)
        {
            for (var i = 0; i < 64; i++)
            {
                if (board[i].Kind == PieceKind.King && board[i].Side == side)
                    return Attacked(board, i, Other(side));
            }
            return false;
        }

        // Whether any piece of by attacks the square, as a move to it would land there. Pawns attack
        // diagonally forward only, so they are checked by where they would stand.
        static bool Attacked(Piece[] board, int square, Side by)
        {
            var file = FileOf(square);
            var rank = RankOf(square);
            var pawnRank = rank + (by == Side.White ? -1 : 1);
            for (var side = -1; side <= 1; side += 2)
            {
                if (IsPiece(board, file + side, pawnRank, by, PieceKind.Pawn))
                    return true;
            }
            for (var i = 0; i < 8; i++)
            {
                if (IsPiece(board, file + s_KnightFiles[i], rank + s_KnightRanks[i], by, PieceKind.Knight))
                    return true;
                if (IsPiece(board, file + s_KingFiles[i], rank + s_KingRanks[i], by, PieceKind.King))
                    return true;
            }
            return SlideHits(board, file, rank, by, s_DiagonalFiles, s_DiagonalRanks, PieceKind.Bishop)
                || SlideHits(board, file, rank, by, s_StraightFiles, s_StraightRanks, PieceKind.Rook);
        }

        static bool IsPiece(Piece[] board, int file, int rank, Side side, PieceKind kind)
        {
            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return false;
            var piece = board[Square(file, rank)];
            return piece.Kind == kind && piece.Side == side;
        }

        static bool SlideHits(Piece[] board, int file, int rank, Side by, int[] files, int[] ranks, PieceKind kind)
        {
            for (var i = 0; i < files.Length; i++)
            {
                var f = file + files[i];
                var r = rank + ranks[i];
                while (f >= 0 && f <= 7 && r >= 0 && r <= 7)
                {
                    var piece = board[Square(f, r)];
                    if (!piece.IsNone)
                    {
                        if (piece.Side == by && (piece.Kind == kind || piece.Kind == PieceKind.Queen))
                            return true;
                        break;
                    }
                    f += files[i];
                    r += ranks[i];
                }
            }
            return false;
        }

        // ---- Repetition ----

        // The position as a string, with whose turn it is, the castling rights and the en passant
        // square that can really be taken, so equal positions count as the same one.
        string Key()
        {
            var chars = new char[64 + 8];
            for (var i = 0; i < 64; i++)
            {
                var piece = m_Squares[i];
                chars[i] = piece.IsNone ? '.' : Letter(piece);
            }
            chars[64] = Turn == Side.White ? 'w' : 'b';
            for (var i = 0; i < 4; i++)
                chars[65 + i] = m_Castle[i] ? '1' : '0';
            var capturable = m_EnPassant >= 0 && EnPassantCapturable();
            chars[69] = capturable ? (char)('a' + FileOf(m_EnPassant)) : '-';
            chars[70] = '-';
            chars[71] = '-';
            return new string(chars);
        }

        bool EnPassantCapturable()
        {
            var pawnRank = RankOf(m_EnPassant) + (Turn == Side.White ? -1 : 1);
            for (var side = -1; side <= 1; side += 2)
            {
                var file = FileOf(m_EnPassant) + side;
                if (file >= 0 && file <= 7 && IsPiece(m_Squares, file, pawnRank, Turn, PieceKind.Pawn))
                {
                    var moves = new List<Move>(4);
                    AddLegalMoves(Square(file, pawnRank), moves);
                    foreach (var move in moves)
                    {
                        if (move.To == m_EnPassant)
                            return true;
                    }
                }
            }
            return false;
        }

        static char Letter(Piece piece)
        {
            var letter = piece.Kind switch
            {
                PieceKind.Pawn => 'p',
                PieceKind.Knight => 'n',
                PieceKind.Bishop => 'b',
                PieceKind.Rook => 'r',
                PieceKind.Queen => 'q',
                _ => 'k',
            };
            return piece.Side == Side.White ? char.ToUpperInvariant(letter) : letter;
        }

        void Record()
        {
            var key = Key();
            m_Seen[key] = m_Seen.TryGetValue(key, out var times) ? times + 1 : 1;
        }

        // Whether the last move taken was a capture, for the demo to say so.
        public bool LastMoveCaptured => m_CapturedLast;
    }

    public static class ChessRules
    {
        public static string Name(int square) => ChessPosition.Name(square);

        // The position after the first count of the moves, played from the start. Taking a move back
        // is this with one fewer: the position keeps no history to step back through.
        public static ChessPosition Replay(IReadOnlyList<Move> moves, int count)
        {
            var position = ChessPosition.Start();
            for (var i = 0; i < count; i++)
                position.Play(moves[i]);
            return position;
        }

        // How many of each piece (by PieceKind index, pawn to queen) a side has lost from the full set.
        // A promoted pawn counts as a lost pawn, and a second queen isn't a negative loss.
        public static int[] Lost(ChessPosition position, Side side)
        {
            var have = new int[6];
            for (var i = 0; i < 64; i++)
            {
                var piece = position[i];
                if (!piece.IsNone && piece.Side == side)
                    have[(int)piece.Kind - 1]++;
            }
            var full = new[] { 8, 2, 2, 2, 1, 1 };
            var lost = new int[6];
            for (var i = 0; i < 5; i++)
                lost[i] = System.Math.Max(0, full[i] - have[i]);
            return lost;
        }

        // "2 pawns, knight", or "nothing" when no piece is lost.
        public static string LostText(int[] lost)
        {
            var names = new[] { "pawn", "knight", "bishop", "rook", "queen" };
            var parts = new List<string>(5);
            for (var i = 0; i < 5; i++)
            {
                if (lost[i] > 0)
                    parts.Add(lost[i] == 1 ? names[i] : $"{lost[i]} {names[i]}s");
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        // The square under a point on the board, for a board of the given square size with white's
        // side at +z (see ChessBoard): the point is in the board's own space, in meters, from its
        // middle. -1 off the board.
        public static int SquareAt(float x, float z, float squareSize)
        {
            var file = (int)System.Math.Floor(x / squareSize + 4f);
            var rank = 7 - (int)System.Math.Floor(z / squareSize + 4f);
            if (file < 0 || file > 7 || rank < 0 || rank > 7)
                return -1;
            return ChessPosition.Square(file, rank);
        }

        // The middle of a square in the board's own space: file across (x), rank away from white (-z).
        public static (float x, float z) SquareCenter(int square, float squareSize) =>
            ((ChessPosition.FileOf(square) - 3.5f) * squareSize, (3.5f - ChessPosition.RankOf(square)) * squareSize);

        // How a side is named in the panel.
        public static string SideName(Side side) => side == Side.White ? "White" : "Black";

        // What the panel says about the game's state, for the side to move.
        public static string Describe(Outcome outcome, Side toMove, bool inCheck)
        {
            switch (outcome)
            {
                case Outcome.Checkmate:
                    return $"Checkmate. {SideName(toMove == Side.White ? Side.Black : Side.White)} wins.";
                case Outcome.Stalemate:
                    return "Stalemate. It's a draw.";
                case Outcome.FiftyMoves:
                    return "Draw: fifty moves with no capture or pawn move.";
                case Outcome.Repetition:
                    return "Draw: the same position three times.";
                case Outcome.DeadPosition:
                    return "Draw: neither side can checkmate.";
                default:
                    return inCheck ? $"{SideName(toMove)} to move. Check!" : $"{SideName(toMove)} to move.";
            }
        }
    }
}
