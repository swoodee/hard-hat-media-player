using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using HardHatMediaPlayer.ViewModels;

namespace HardHatMediaPlayer.Controls;

/// <summary>
/// The library list. Rows are exposed to screen readers and UI Automation by their label
/// and can be activated (Invoke), the same as clicking them.
/// </summary>
public sealed class RowList : ListBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RowListPeer(this);

    private sealed class RowListPeer : ListBoxAutomationPeer
    {
        public RowListPeer(ListBox owner) : base(owner) { }

        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new RowPeer(item, this);
    }

    private sealed class RowPeer : ListBoxItemAutomationPeer, IInvokeProvider
    {
        public RowPeer(object item, SelectorAutomationPeer owner) : base(item, owner) { }

        protected override string GetNameCore() => Item switch
        {
            ItemRow row => row.Label,
            SectionRow section => section.Title,
            _ => base.GetNameCore(),
        };

        public override object GetPattern(PatternInterface pattern) =>
            pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);

        public void Invoke()
        {
            // Deferred like WPF's own button peers, so a row that opens a dialog doesn't block the automation client.
            if (Item is PanelRow row) System.Windows.Application.Current.Dispatcher.BeginInvoke(row.Activate);
        }
    }
}
