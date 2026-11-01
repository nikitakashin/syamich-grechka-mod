using System.Collections.Generic;
using System.Text;

namespace Grechka.Quests
{
    // Editor-only snapshots. Undo/redo must never touch live quest progress or rewards.
    public sealed class DraftHistory
    {
        const int MaxEntries = 50;
        const int MaxHistoryBytes = 8 * 1024 * 1024;
        readonly List<string> undo = new List<string>(), redo = new List<string>();
        string current;
        int undoBytes;
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public DraftHistory(DraftDocument initial) { current = DraftStore.Encode(initial); }

        public void Record(DraftDocument next)
        {
            string encoded = DraftStore.Encode(next);
            if (encoded == current) return;
            undo.Add(current); undoBytes += Encoding.UTF8.GetByteCount(current); current = encoded; redo.Clear(); Trim();
        }

        public DraftDocument Undo()
        {
            if (!CanUndo) return null;
            redo.Add(current); current = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            undoBytes -= Encoding.UTF8.GetByteCount(current);
            return DraftStore.Decode(current);
        }

        public DraftDocument Redo()
        {
            if (!CanRedo) return null;
            undo.Add(current); undoBytes += Encoding.UTF8.GetByteCount(current);
            current = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); Trim();
            return DraftStore.Decode(current);
        }

        void Trim()
        {
            while (undo.Count > 0 && (undo.Count > MaxEntries || undoBytes > MaxHistoryBytes))
            {
                undoBytes -= Encoding.UTF8.GetByteCount(undo[0]); undo.RemoveAt(0);
            }
        }
    }
}
