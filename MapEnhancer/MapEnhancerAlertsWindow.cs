using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using UI;
using UI.Builder;
using UI.Map;
using UI.Windows;
using UnityEngine;
using MapEnhancer.UMM;

namespace MapEnhancer
{
	/// <summary>Native Railroader window for reviewing and navigating active HOPA alerts.</summary>
	internal static class MapEnhancerAlertsWindow
	{
		private const string Identifier = "reaper.mapenhancer.rollingstockalerts";
		private static IDisposable? _toolbarHandle;
		private static readonly HashSet<int> ExpandedGroups = new HashSet<int>();
		private static readonly HashSet<HopaAlertType> SelectedTypes = new HashSet<HopaAlertType>();
		private static Action? _refreshPanel;
		private static int _sortMode;

		public static void Register()
		{
			if (WindowManager.Shared == null) return;
			WindowManager.Shared.RegisterCustomWindow(new CustomWindowDefinition
			{
				Identifier = Identifier,
				Title = "Rolling Stock Alerts",
				Config = WindowConfig.Resizable(new Vector2Int(600, 700), new Vector2Int(440, 360), Window.Position.Center),
				BuilderAction = Build
			});
			_toolbarHandle = MapToolbar.Add(MapToolbarSide.Left, 11, builder =>
				builder.AddButtonCompact("Alerts", Toggle).Tooltip("Rolling Stock Alerts", "Review active HOPA alerts by train and vehicle."));
			HopaAlertManager.Changed -= OnAlertsChanged;
			HopaAlertManager.Changed += OnAlertsChanged;
		}

		public static void Unregister()
		{
			HopaAlertManager.Changed -= OnAlertsChanged;
			_refreshPanel = null;
			_toolbarHandle?.Dispose();
			_toolbarHandle = null;
			if (WindowManager.Shared != null) WindowManager.Shared.CloseWindow(Identifier);
		}

		public static void Toggle()
		{
			WindowManager.Shared?.ToggleWindow(Identifier);
		}

		private static void OnAlertsChanged()
		{
			_refreshPanel?.Invoke();
			MapEnhancer.Instance?.UpdateHopaSummary();
		}

		private static void Build(UIPanelBuilder builder)
		{
			builder.VScrollView(scroll =>
			{
				_refreshPanel = scroll.SetNeedsRebuild;
				scroll.AddTitle("Rolling Stock Alerts", "Active Hotbox, Oil, Handbrake, Air Leak, and Derailment conditions");
				scroll.AddSection("Current Status");
				scroll.AddField("Active alert occurrences", HopaAlertManager.TotalOccurrences.ToString());
				scroll.AddField("Applied Handbrake occurrences", HopaAlertManager.HandbrakeOccurrences.ToString());
				scroll.AddField("Affected vehicles", HopaAlertManager.AffectedVehicles.ToString());
				scroll.AddField("Affected trains / consists", HopaAlertManager.AffectedConsists.ToString());
				scroll.AddSection("Window Options");
				scroll.AddFieldToggle("Group vehicles by train / consist", () => Loader.Settings.GroupHopaAlertsByConsist, value =>
				{
					Loader.Settings.GroupHopaAlertsByConsist = value;
					Loader.Settings.OnChange();
					Loader.Settings.Save();
					_refreshPanel?.Invoke();
				}, true);
				scroll.AddFieldToggle("Keep window open after selecting a vehicle", () => Loader.Settings.KeepHopaAlertsWindowOpenAfterSelection, value =>
				{
					Loader.Settings.KeepHopaAlertsWindowOpenAfterSelection = value;
					Loader.Settings.Save();
				}, true);

				scroll.AddSection("Alert Filters (match any selected type)");
				AddFilter(scroll, "Hotbox", HopaAlertType.Hotbox, HopaAlertManager.HotboxColor);
				AddFilter(scroll, "Low Journal Oil", HopaAlertType.LowOil, HopaAlertManager.LowOilColor);
				AddFilter(scroll, "Applied Handbrake", HopaAlertType.Handbrake, HopaAlertManager.HandbrakeColor);
				AddFilter(scroll, "Air Leak", HopaAlertType.AirLeak, HopaAlertManager.AirLeakColor);
				AddFilter(scroll, "Derailment", HopaAlertType.Derailment, HopaAlertManager.DerailmentColor);
				scroll.AddButton("Select All Alert Types", () =>
				{
					SelectedTypes.Clear();
					SelectedTypes.UnionWith(AllTypes());
					_refreshPanel?.Invoke();
				});
				scroll.AddButton("Clear Alert Type Filters", () =>
				{
					SelectedTypes.Clear();
					_refreshPanel?.Invoke();
				});

				var options = new List<TMPro.TMP_Dropdown.OptionData>
				{
					new TMPro.TMP_Dropdown.OptionData("Alert priority"),
					new TMPro.TMP_Dropdown.OptionData("Alert type"),
					new TMPro.TMP_Dropdown.OptionData("Vehicle identification"),
					new TMPro.TMP_Dropdown.OptionData("Train / consist")
				};
				scroll.AddField("Sort by", scroll.AddDropdown(options, _sortMode, idx => { _sortMode = idx; _refreshPanel?.Invoke(); }));

				scroll.AddSection("Affected Rolling Stock");
				var visibleStates = HopaAlertManager.ActiveAlerts
					.Where(s => s.Car != null && (SelectedTypes.Count == 0 || SelectedTypes.Any(t => (s.ActiveAlerts & t) != 0)))
					.ToList();
				var visible = Loader.Settings.GroupHopaAlertsByConsist
					? visibleStates.GroupBy(s => s.GroupId).ToList()
					: visibleStates.GroupBy(s => -s.Car.GetInstanceID()).ToList();
				scroll.AddButton("Expand All Groups", () =>
				{
					foreach (var group in visible) ExpandedGroups.Add(group.Key);
					_refreshPanel?.Invoke();
				});
				scroll.AddButton("Collapse All Groups", () =>
				{
					ExpandedGroups.Clear();
					_refreshPanel?.Invoke();
				});
				IEnumerable<IGrouping<int, HopaAlertState>> groups = _sortMode switch
				{
					1 => visible.OrderBy(g => g.Min(s => TypeOrder(s.ActiveAlerts))),
					2 => visible.OrderBy(g => VehicleName(g.First().Car), StringComparer.OrdinalIgnoreCase),
					3 => visible.OrderBy(g => g.First().ConsistName, StringComparer.OrdinalIgnoreCase),
					_ => visible.OrderBy(g => g.Min(s => Priority(s.ActiveAlerts))).ThenByDescending(g => g.Sum(s => s.AlertCount))
				};

				foreach (var group in groups)
				{
					var states = SortStates(group).ToList();
					string groupName = Loader.Settings.GroupHopaAlertsByConsist ? states[0].ConsistName : VehicleName(states[0].Car);
					int alerts = states.Sum(s => s.AlertCount);
					int handbrakes = states.Sum(s => s.HandbrakeCount);
					int connected = Loader.Settings.GroupHopaAlertsByConsist ? states[0].ConnectedCount : 1;
					string heading = $"{groupName} — {alerts} alerts, {handbrakes} handbrakes, {states.Count} affected / {connected} connected vehicles";
					scroll.AddButton((ExpandedGroups.Contains(group.Key) ? "▼ " : "▶ ") + heading, () =>
					{
						if (!ExpandedGroups.Add(group.Key)) ExpandedGroups.Remove(group.Key);
						_refreshPanel?.Invoke();
					});
					if (!ExpandedGroups.Contains(group.Key)) continue;
					foreach (var state in states)
					{
						Car car = state.Car;
						scroll.AddField("Vehicle", VehicleName(car));
						string countText = state.HandbrakeCount > 0
							? $"{state.AlertCount} alerts, {state.HandbrakeCount} handbrake"
							: $"{state.AlertCount} alerts";
						scroll.AddField($"Active alerts ({countText})", FormatAlerts(state.ActiveAlerts));
						scroll.AddButton("Locate on Map", () => MapEnhancer.FocusOnCar(car, false));
						scroll.AddButton("Highlight on Map", () => MapEnhancer.HighlightCarOnMap(car));
						scroll.AddButton("Select Vehicle", () =>
						{
							MapEnhancer.FocusOnCar(car, true);
							if (!Loader.Settings.KeepHopaAlertsWindowOpenAfterSelection) WindowManager.Shared?.CloseWindow(Identifier);
						});
					}
				}
			});
		}

		private static IEnumerable<HopaAlertState> SortStates(IEnumerable<HopaAlertState> states)
		{
			return _sortMode switch
			{
				1 => states.OrderBy(s => TypeOrder(s.ActiveAlerts)).ThenBy(s => VehicleName(s.Car), StringComparer.OrdinalIgnoreCase),
				2 => states.OrderBy(s => VehicleName(s.Car), StringComparer.OrdinalIgnoreCase),
				3 => states.OrderBy(s => s.ConsistName, StringComparer.OrdinalIgnoreCase),
				_ => states.OrderBy(s => Priority(s.ActiveAlerts)).ThenBy(s => VehicleName(s.Car), StringComparer.OrdinalIgnoreCase)
			};
		}

		private static int Priority(HopaAlertType type)
		{
			if ((type & HopaAlertType.Derailment) != 0) return 0;
			if ((type & HopaAlertType.Hotbox) != 0) return 0;
			if ((type & HopaAlertType.AirLeak) != 0) return 1;
			if ((type & HopaAlertType.LowOil) != 0) return 2;
			return 3;
		}

		private static int TypeOrder(HopaAlertType type)
		{
			if ((type & HopaAlertType.Hotbox) != 0) return 0;
			if ((type & HopaAlertType.LowOil) != 0) return 1;
			if ((type & HopaAlertType.Handbrake) != 0) return 2;
			if ((type & HopaAlertType.AirLeak) != 0) return 3;
			return 4;
		}

		private static void AddFilter(UIPanelBuilder scroll, string name, HopaAlertType type, Color color)
		{
			int count = HopaAlertManager.ActiveAlerts.Count(s => (s.ActiveAlerts & type) != 0);
			scroll.AddFieldToggle($"{name} ({count})", () => SelectedTypes.Contains(type), value =>
			{
				if (value) SelectedTypes.Add(type); else SelectedTypes.Remove(type);
				_refreshPanel?.Invoke();
			}, true).Tooltip(name, $"Filter to vehicles with this alert. Color: #{ColorUtility.ToHtmlStringRGB(color)}");
		}

		private static IEnumerable<HopaAlertType> AllTypes()
		{
			yield return HopaAlertType.Hotbox;
			yield return HopaAlertType.LowOil;
			yield return HopaAlertType.Handbrake;
			yield return HopaAlertType.AirLeak;
			yield return HopaAlertType.Derailment;
		}

		private static string VehicleName(Car car)
		{
			if (car == null) return string.Empty;
			string reporting = car.Ident.ReportingMark ?? string.Empty;
			string number = car.Ident.RoadNumber ?? string.Empty;
			string identification = (reporting + " " + number).Trim();
			return !string.IsNullOrWhiteSpace(identification) ? identification : (car.DisplayName ?? car.id ?? string.Empty);
		}

		private static string FormatAlerts(HopaAlertType type)
		{
			var values = new List<string>(5);
			if ((type & HopaAlertType.Hotbox) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HopaAlertManager.HotboxColor)}>H Hotbox</color>");
			if ((type & HopaAlertType.LowOil) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HopaAlertManager.LowOilColor)}>O Low Journal Oil</color>");
			if ((type & HopaAlertType.Handbrake) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HopaAlertManager.HandbrakeColor)}>P Applied Handbrake</color>");
			if ((type & HopaAlertType.AirLeak) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HopaAlertManager.AirLeakColor)}>A Air Leak</color>");
			if ((type & HopaAlertType.Derailment) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HopaAlertManager.DerailmentColor)}>D Derailment</color>");
			return string.Join(", ", values);
		}
	}
}
