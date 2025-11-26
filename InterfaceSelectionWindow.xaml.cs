using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;

namespace MoxaConfigApp;

public partial class InterfaceSelectionWindow : Window
{
    public NetworkInterface? SelectedInterface { get; private set; }

    public InterfaceSelectionWindow(IEnumerable<NetworkInterfaceItem> interfaces)
    {
        InitializeComponent();
        lstInterfaces.ItemsSource = interfaces.ToList();

        if (lstInterfaces.Items.Count > 0)
        {
            lstInterfaces.SelectedIndex = 0;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (lstInterfaces.SelectedItem is NetworkInterfaceItem item)
        {
            SelectedInterface = item.Interface;
            DialogResult = true;
        }
        else
        {
            DialogResult = false;
        }

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void lstInterfaces_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (lstInterfaces.SelectedItem is NetworkInterfaceItem)
        {
            Confirm_Click(sender, e);
        }
    }
}
