using System.Collections.ObjectModel;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;


namespace RustPlusDesk.Views
{


    public partial class DeviceImportWindow : Window
    {
        public ObservableCollection<DeviceImportItem> Devices { get; } = new();
        private readonly Func<uint, Task<EntityProbeResult>> _probe;

        public DeviceImportWindow(
            List<DeviceImportItem> devices,
            Func<uint, Task<EntityProbeResult>> probe)
        {
            InitializeComponent();

            // Liste in ObservableCollection kopieren
            Devices.Clear();
            foreach (var d in devices)
                Devices.Add(d);

            _probe = probe;
            DataContext = this;
        }

        private async void BtnCheckStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
                fe.IsEnabled = false;

            try
            {
                // 1) Alle Devices nach EntityId gruppieren (Duplikate vermeiden)
                var groups = Devices
                    .GroupBy(d => d.EntityId)
                    .ToList();

                // Cache für Ergebnisse
                var cache = new Dictionary<uint, EntityProbeResult>();

                int probedCount = 0;

                // Flatten all devices including children of groups to check them
                var allDevicesToCheck = new List<DeviceImportItem>();
                foreach (var item in Devices)
                {
                    if (item.OriginalDto?.IsGroup == true || item.Kind == "Group")
                    {
                        item.ExistsState = "ok"; // Groups are always ok
                        // We also need to check the children of the group
                        if (item.OriginalDto?.Children != null)
                        {
                            foreach (var childDto in item.OriginalDto.Children)
                            {
                                // Create a temporary import item just for checking
                                allDevicesToCheck.Add(new DeviceImportItem
                                {
                                    EntityId = childDto.EntityId,
                                    Kind = childDto.Kind,
                                    OriginalDto = childDto,
                                    // Keep reference to parent item to update its state if child is missing
                                    Name = item.EntityId.ToString() // Use Name as a hack to store parent ID
                                });
                            }
                        }
                    }
                    else
                    {
                        allDevicesToCheck.Add(item);
                    }
                }

                var groupsToCheck = allDevicesToCheck
                    .GroupBy(d => d.EntityId)
                    .ToList();

                foreach (var group in groupsToCheck)
                {
                    var id = group.Key;

                    EntityProbeResult result;

                    if (!cache.TryGetValue(id, out result))
                    {
                        try
                        {
                            // tatsächlicher Probe-Call
                            result = await _probe(id);
                        }
                        catch
                        {
                            result = new EntityProbeResult(false, null, null);
                        }

                        cache[id] = result;

                        probedCount++;

                        // kleine Pause nach jedem Request
                        await Task.Delay(80);

                        // zusätzliche Pause nach jeweils 5 Geräten
                        if (probedCount % 5 == 0)
                            await Task.Delay(250);
                    }

                    // 2) Ergebnis auf alle Items mit dieser ID anwenden
                    var state = result.Exists ? "ok" : "missing";

                    foreach (var item in group)
                    {
                        if (item.OriginalDto != null && uint.TryParse(item.Name, out var parentId))
                        {
                            // This was a child, update parent's state if child is missing
                            var parentItem = Devices.FirstOrDefault(d => d.EntityId == parentId);
                            if (parentItem != null && state == "missing")
                            {
                                parentItem.ExistsState = "missing";
                            }
                        }
                        else
                        {
                            item.ExistsState = state;
                        }
                    }
                }
            }
            finally
            {
                if (sender is FrameworkElement fe2)
                    fe2.IsEnabled = true;
            }
        }

        public IReadOnlyList<DeviceImportItem> SelectedItems =>
            Devices.Where(d => d.IsSelected && !d.AlreadyPresent).ToList();



        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in Devices)
            {
                if (!it.AlreadyPresent)
                    it.IsSelected = true;
            }
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in Devices)
                it.IsSelected = false;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedItems.Any())
            {
                DialogResult = false;
                return;
            }

            DialogResult = true;
        }
    }

}
