using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NexaConnect.POS;

public partial class TouchKeyboard : UserControl
{
    private bool uppercase;
    private bool symbols;
    private bool numeric;
    public event Action<string>? KeyPressed;
    public event Action? Dismissed;

    public TouchKeyboard() { InitializeComponent(); }

    public void Configure(string label, bool amount)
    {
        FieldLabel.Text = label;
        numeric = amount; uppercase = false; symbols = false;
        RenderKeys();
    }

    private void RenderKeys()
    {
        KeyRows.Children.Clear();
        string[] rows = numeric ? ["7 8 9", "4 5 6", "1 2 3", "0 . ⌫"]
            : symbols ? ["1 2 3 4 5 6 7 8 9 0", "! @ # $ % & * ( ) _", "- + = / : ; , . ? '"]
            : ["1 2 3 4 5 6 7 8 9 0", "q w e r t y u i o p", "a s d f g h j k l", "z x c v b n m - ."];
        foreach (string row in rows)
        {
            var grid = new UniformGrid { Rows = 1, Columns = row.Split(' ').Length };
            foreach (string key in row.Split(' ')) AddKey(grid, uppercase ? key.ToUpperInvariant() : key);
            KeyRows.Children.Add(grid);
        }
        var actions = new UniformGrid { Rows = 1, Columns = numeric ? 1 : 5 };
        if (!numeric) { AddKey(actions, "Shift"); AddKey(actions, symbols ? "ABC" : "Symbols"); AddKey(actions, "Space"); AddKey(actions, "⌫"); }
        AddKey(actions, "Clear");
        KeyRows.Children.Add(actions);
    }

    private void AddKey(Panel row, string value)
    {
        var key = new Button { Content = value, Focusable = false, MinHeight = 48,
            Padding = new Thickness(4), Margin = new Thickness(2), FontSize = 18 };
        System.Windows.Automation.AutomationProperties.SetName(key, value == "⌫" ? "Backspace" : value);
        key.Click += (_, _) => {
            if (value == "Shift") { uppercase = !uppercase; RenderKeys(); }
            else if (value is "Symbols" or "ABC") { symbols = !symbols; RenderKeys(); }
            else KeyPressed?.Invoke(value == "Space" ? " " : value);
        };
        row.Children.Add(key);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke();
}
