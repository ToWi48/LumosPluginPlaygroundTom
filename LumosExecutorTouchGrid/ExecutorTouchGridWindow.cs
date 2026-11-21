using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Lumos.GUI;
using Lumos.GUI.BaseWindow;
using LumosLIB.GUI.Windows;

namespace LumosExecutorTouchGrid
{
    /// <summary>Dockable DMXControl window (like the executor window) hosting the WPF touch grid.</summary>
    public class ExecutorTouchGridWindow : ToolWindow
    {
        private readonly ElementHost _host;
        private readonly GridModel _model;
        private readonly TouchGridView _view;

        public override EMenuGroups MenuGroup => EMenuGroups.Control;

        public ExecutorTouchGridWindow()
        {
            Text = "Executor Touch";
            TabText = "Executor Touch";
            Name = "ExecutorTouchGridWindow";
            MainFormMenu = MenuType.Windows;
            MenuIconKey = "cube_grey";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(900, 500);

            _model = new GridModel();
            _view = new TouchGridView(_model);
            _host = new ElementHost { Dock = DockStyle.Fill, Child = _view };
            Controls.Add(_host);
            _model.Start(150);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _view.ReleaseAll();
                _model.Dispose();
                _host.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
