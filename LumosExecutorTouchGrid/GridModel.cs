using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Lumos.GUI.Executor;
using Lumos.GUI.Facade.Executor;
using Lumos.GUI.Facade.SceneList;
using LumosLIB.Kernel.Log;
using LumosPluginShared;
using org.dmxc.lumos.Kernel.Executor;

namespace LumosExecutorTouchGrid
{
    /// <summary>Everything one tile shows. Immutable snapshot, compared to avoid needless UI updates.</summary>
    internal sealed class CellData : IEquatable<CellData>
    {
        public IExecutorFacade Executor;
        public string ExecutorId;
        public bool Empty;
        public string Title;
        public string TypeLabel;
        public string Info;
        public string Counter;
        public string ButtonLabel;
        public Color Color;
        public bool Active;

        public bool Equals(CellData o)
        {
            return o != null && o.ExecutorId == ExecutorId && o.Empty == Empty && o.Title == Title && o.TypeLabel == TypeLabel
                   && o.Info == Info && o.Counter == Counter && o.ButtonLabel == ButtonLabel && o.Color == Color && o.Active == Active;
        }

        public override bool Equals(object obj) => Equals(obj as CellData);

        public override int GetHashCode() => (ExecutorId ?? "").GetHashCode();
    }

    internal sealed class RowData
    {
        public string PageId;
        public string PageName;
        public uint PageNumber;
        public List<CellData> Cells = new List<CellData>();
    }

    internal sealed class GridSnapshot
    {
        public List<RowData> Rows = new List<RowData>();

        public int Columns => Math.Max(1, Rows.Count == 0 ? 1 : Rows.Max(r => r.Cells.Count));

        /// <summary>Changes only when pages / executors are added, removed or reordered.</summary>
        public string StructureKey
        {
            get
            {
                var sb = new StringBuilder();
                foreach (RowData r in Rows)
                {
                    sb.Append(r.PageId).Append(':').Append(r.Cells.Count).Append(';');
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Reads executor pages / executors / members from the GUI facades on a background thread
    /// (some facade getters block, which must never happen on the UI thread) and publishes snapshots.
    /// </summary>
    internal sealed class GridModel : IDisposable
    {
        private static readonly ILumosLog Log = LumosLogger.getInstance(nameof(GridModel));

        private readonly ConcurrentDictionary<Type, PropertyInfo> _nameProps = new ConcurrentDictionary<Type, PropertyInfo>();

        private Timer _timer;
        private int _busy;

        public event Action<GridSnapshot> SnapshotReady;

        /// <summary>Set by the view: no work while the window is hidden.</summary>
        public volatile bool Visible;

        public void Start(int intervalMs)
        {
            _timer = new Timer(_ => Tick(), null, 200, intervalMs);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void Tick()
        {
            if (!Visible || Interlocked.Exchange(ref _busy, 1) == 1)
            {
                return;
            }
            try
            {
                SnapshotReady?.Invoke(Build());
            }
            catch (Exception e)
            {
                Log.Debug("TouchGrid: building snapshot failed: {0}", e.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private GridSnapshot Build()
        {
            var snap = new GridSnapshot();
            foreach (IExecutorPageFacade page in ExecutorManager.getInstance().ExecutorPages.OrderBy(p => p.PageNumber))
            {
                var row = new RowData
                {
                    PageId = page.ID,
                    PageNumber = page.PageNumber,
                    PageName = string.IsNullOrWhiteSpace(page.Name) ? "Page " + page.PageNumber : page.Name
                };
                // cache only - see ExecutorCache why GUIExecutors must not be used here
                foreach (IExecutorFacade ex in ExecutorCache.GetExecutors(page))
                {
                    row.Cells.Add(ex == null ? new CellData { Empty = true, ExecutorId = "?" + row.Cells.Count } : BuildCell(ex));
                }
                snap.Rows.Add(row);
            }
            return snap;
        }

        private CellData BuildCell(IExecutorFacade ex)
        {
            var cell = new CellData { Executor = ex, ExecutorId = ex.ID };
            string wrapperId = ex.ExecutorMemberID;
            if (string.IsNullOrEmpty(wrapperId))
            {
                cell.Empty = true;
                cell.Title = ex.Name;
                return cell;
            }
            cell.Color = ex.Color;
            cell.ButtonLabel = ex.Button1DisplayName;
            IGUIExecutorMember member = ExecutorCache.GetMember(ex);
            cell.Title = MemberName(member) ?? ex.Name;
            cell.Active = ex.Button1Active;

            if (member is ISceneListFacade cl)
            {
                string state = cl.State.ToString();
                bool running = state != "Stopped";
                cell.Active = running;
                cell.TypeLabel = running ? "CUELIST · " + state.ToUpperInvariant() : "CUELIST";
                IReadOnlyList<Lumos.GUI.Facade.Scene.ISceneFacade> scenes = null;
                try
                {
                    scenes = cl.Scenes;
                }
                catch
                {
                    // not loaded yet
                }
                int count = scenes?.Count ?? 0;
                if (running && cl.Index >= 0 && cl.Index < count)
                {
                    cell.Info = scenes[cl.Index]?.Name;
                    cell.Counter = (cl.Index + 1) + "/" + count;
                }
                else if (count > 0)
                {
                    int next = cl.NextIndex >= 0 && cl.NextIndex < count ? cl.NextIndex : 0;
                    cell.Info = "▸ " + scenes[next]?.Name;
                    cell.Counter = count + (count == 1 ? " Cue" : " Cues");
                }
            }
            else if (member != null)
            {
                cell.TypeLabel = TypeLabel(member);
                cell.Info = ex.Button1Active ? "aktiv" : null;
            }
            else
            {
                cell.TypeLabel = "…";
            }
            return cell;
        }

        private static string TypeLabel(object member)
        {
            string n = member.GetType().Name;
            if (n.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0) return "CUELIST GROUP";
            if (n.IndexOf("Macro", StringComparison.OrdinalIgnoreCase) >= 0) return "MACRO";
            if (n.IndexOf("Master", StringComparison.OrdinalIgnoreCase) >= 0) return "MASTER";
            if (n.IndexOf("Timecode", StringComparison.OrdinalIgnoreCase) >= 0) return "TIMECODE";
            return n.Replace("Facade", "").ToUpperInvariant();
        }

        private string MemberName(object member)
        {
            if (member == null)
            {
                return null;
            }
            try
            {
                PropertyInfo p = _nameProps.GetOrAdd(member.GetType(), t => t.GetProperty("Name", typeof(string)));
                return p?.GetValue(member) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
