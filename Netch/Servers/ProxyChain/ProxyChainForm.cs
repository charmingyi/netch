using Netch.Models;
using Netch.Utils;
using Microsoft.VisualStudio.Threading;

namespace Netch.Servers;

public sealed class ProxyChainForm : Form
{
    private readonly ProxyChainServer _chain;
    private readonly ListBox _available = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly ListBox _selected = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Top };

    public ProxyChainForm(ProxyChainServer? chain = null)
    {
        _chain = chain ?? new ProxyChainServer();
        Text = chain == null ? "Create proxy chain" : "Edit proxy chain";
        Width = 760;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        _name.Text = _chain.Remark;
        foreach (var server in Global.Settings.Server.Where(server => server is not ProxyChainServer && !ReferenceEquals(server, chain)))
            _available.Items.Add(server);
        foreach (var node in _chain.Nodes)
            _selected.Items.Add(node);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(new Label { Text = "Name", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(_name, 1, 0);
        root.SetColumnSpan(_name, 2);
        root.Controls.Add(new Label { Text = "Available nodes", TextAlign = ContentAlignment.BottomLeft, Dock = DockStyle.Fill }, 0, 1);
        root.Controls.Add(new Label { Text = "Chain order: entry → exit", TextAlign = ContentAlignment.BottomLeft, Dock = DockStyle.Fill }, 2, 1);
        root.Controls.Add(_available, 0, 2);
        root.Controls.Add(_selected, 2, 2);

        var middle = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var add = new Button { Text = "Add →", Width = 78 };
        var remove = new Button { Text = "← Remove", Width = 78 };
        add.Click += (_, _) => AddSelected();
        remove.Click += (_, _) => RemoveSelected();
        middle.Controls.Add(add);
        middle.Controls.Add(remove);
        root.Controls.Add(middle, 1, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var up = new Button { Text = "Move up", Width = 80 };
        var down = new Button { Text = "Move down", Width = 80 };
        save.Click += (_, _) => SaveChain();
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        footer.Controls.Add(down);
        footer.Controls.Add(up);
        root.Controls.Add(footer, 0, 3);
        root.SetColumnSpan(footer, 3);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void AddSelected()
    {
        foreach (var node in _available.SelectedItems.Cast<Server>().ToArray())
            if (!_selected.Items.Contains(node))
                _selected.Items.Add(node);
    }

    private void RemoveSelected()
    {
        var selected = _selected.SelectedIndices.Cast<int>().OrderByDescending(index => index).ToArray();
        foreach (var index in selected)
            _selected.Items.RemoveAt(index);
    }

    private void MoveSelected(int direction)
    {
        var index = _selected.SelectedIndex;
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= _selected.Items.Count)
            return;

        var node = _selected.Items[index];
        _selected.Items.RemoveAt(index);
        _selected.Items.Insert(destination, node);
        _selected.SelectedIndex = destination;
    }

    private void SaveChain()
    {
        var nodes = _selected.Items.Cast<Server>().ToList();
        if (string.IsNullOrWhiteSpace(_name.Text) || nodes.Count is < 2 or > 5)
        {
            MessageBox.Show(this, "Enter a name and select between two and five nodes.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _chain.Remark = _name.Text.Trim();
        _chain.Nodes = nodes;
        if (!Global.Settings.Server.Contains(_chain))
        {
            _chain.Group = Constants.DefaultGroup;
            Global.Settings.Server.Add(_chain);
        }

        Configuration.SaveAsync().Forget();
        DialogResult = DialogResult.OK;
        Close();
    }
}
