using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Model;
using MapEnhancer.UMM;
using RollingStock;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace MapEnhancer
{
	[Flags]
	internal enum HopaAlertType
	{
		None = 0,
		Hotbox = 1,
		LowOil = 2,
		Handbrake = 4,
		AirLeak = 8,
		Derailment = 16
	}

	internal sealed class HopaAlertState
	{
		public Car Car = null!;
		public HopaAlertType ActiveAlerts;
		public int LastGroupId;
		public int LastConnectedCount;
		public string LastGroupName = string.Empty;
		public string LastVehicleName = string.Empty;
		public int AlertCount => CountBits((int)(ActiveAlerts & ~HopaAlertType.Handbrake));
		public int HandbrakeCount => (ActiveAlerts & HopaAlertType.Handbrake) != 0 ? 1 : 0;
		public string VehicleName => Car == null ? string.Empty : (Car.DisplayName ?? Car.Ident.ReportingMark ?? Car.Ident.RoadNumber ?? string.Empty);
		public string ConsistName
		{
			get
			{
				if (Car == null) return "Loose / unassigned";
				var set = Car.set;
				if (set?.Cars == null) return "Loose / unassigned";
				var lead = set.Cars.FirstOrDefault(c => c != null);
				return lead == null ? "Loose / unassigned" : (lead.DisplayName ?? lead.Ident.ReportingMark ?? lead.Ident.RoadNumber ?? "Train / consist");
			}
		}
		public int GroupId
		{
			get
			{
				if (Car == null) return 0;
				return HopaAlertManager.GetGroupId(Car);
			}
		}
		public int ConnectedCount => Car?.set?.Cars?.Count() ?? 1;

		private static int CountBits(int value)
		{
			int count = 0;
			while (value != 0) { count += value & 1; value >>= 1; }
			return count;
		}
	}

	/// <summary>Owns the current HOPA state independently of map indicators and alert-window presentation.</summary>
	internal static class HopaAlertManager
	{
		public static readonly Color HotboxColor = new Color(0.94f, 0.24f, 0.24f, 1f);
		public static readonly Color LowOilColor = new Color(1f, 0.60f, 0f, 1f);
		public static Color HandbrakeColor { get; private set; } = new Color(0.62f, 0.25f, 0.82f, 1f);
		public static readonly Color AirLeakColor = new Color(0.16f, 0.71f, 0.96f, 1f);
		public static readonly Color DerailmentColor = new Color(1f, 0.18f, 0.05f, 1f);
		private static readonly Dictionary<int, HopaAlertState> ActiveByCar = new Dictionary<int, HopaAlertState>();
		private static readonly HashSet<int> SeenCarIds = new HashSet<int>();
		private static readonly List<int> RemovedCarIds = new List<int>();
		public static IReadOnlyCollection<HopaAlertState> ActiveAlerts => ActiveByCar.Values;
		public static int TotalOccurrences => ActiveByCar.Values.Sum(s => s.AlertCount);
		public static int HandbrakeOccurrences => ActiveByCar.Values.Sum(s => s.HandbrakeCount);
		public static int AffectedVehicles => ActiveByCar.Values.Count(s => s.AlertCount > 0);
		public static int AffectedConsists => ActiveByCar.Values.Where(s => s.AlertCount > 0).Select(s => s.GroupId).Where(id => id > 0).Distinct().Count();
		public static event Action? Changed;

		public static void InitializeNativeColors()
		{
			var display = Resources.FindObjectsOfTypeAll<TrainBrakeDisplay>().FirstOrDefault();
			if (display == null)
			{
				Loader.LogDebug("Native handbrake color display was unavailable; using purple fallback.");
				return;
			}
			if (HandbrakeColor != display.handbrakeAppliedColor)
			{
				HandbrakeColor = display.handbrakeAppliedColor;
				Changed?.Invoke();
			}
		}

		internal static int GetGroupId(Car car)
		{
			var lead = car.set?.Cars?.FirstOrDefault(c => c != null);
			return lead != null ? lead.GetInstanceID() : 0;
		}

		private static string GetGroupName(Car car)
		{
			var lead = car.set?.Cars?.FirstOrDefault(c => c != null);
			return lead == null ? "Loose / unassigned" : (lead.DisplayName ?? lead.Ident.ReportingMark ?? lead.Ident.RoadNumber ?? "Train / consist");
		}

		private static string GetVehicleName(Car car)
		{
			string identification = ((car.Ident.ReportingMark ?? string.Empty) + " " + (car.Ident.RoadNumber ?? string.Empty)).Trim();
			return !string.IsNullOrWhiteSpace(identification) ? identification : (car.DisplayName ?? car.id ?? string.Empty);
		}

		public static bool TryGet(Car car, out HopaAlertState state)
		{
			if (car != null && ActiveByCar.TryGetValue(car.GetInstanceID(), out state!)) return true;
			state = null!;
			return false;
		}

		public static string FormatAlerts(Car car)
		{
			if (!TryGet(car, out var state)) return string.Empty;

			return FormatAlertTypes(state.ActiveAlerts);
		}

		public static string FormatAlertsForConsist(IEnumerable<Car> cars)
		{
			var instanceIds = new HashSet<int>();
			var stableIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (var car in cars)
			{
				if (car == null) continue;
				instanceIds.Add(car.GetInstanceID());
				if (!string.IsNullOrEmpty(car.id)) stableIds.Add(car.id);
			}

			var lines = new List<string>();
			foreach (var state in ActiveByCar.Values)
			{
				if (state.Car == null ||
					(!instanceIds.Contains(state.Car.GetInstanceID()) &&
						(string.IsNullOrEmpty(state.Car.id) || !stableIds.Contains(state.Car.id))))
				{
					continue;
				}

				string name = state.Car.DisplayName ??
					$"{state.Car.Ident.ReportingMark} {state.Car.Ident.RoadNumber}";
				if (string.IsNullOrWhiteSpace(name)) name = state.VehicleName;
				lines.Add($"<b>{name}</b>\n{FormatAlertTypes(state.ActiveAlerts)}");
			}

			return string.Join("\n", lines);
		}

		private static string FormatAlertTypes(HopaAlertType alerts)
		{
			var values = new List<string>(5);
			if ((alerts & HopaAlertType.Hotbox) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HotboxColor)}>H Hotbox</color>");
			if ((alerts & HopaAlertType.LowOil) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(LowOilColor)}>O Low Journal Oil</color>");
			if ((alerts & HopaAlertType.Handbrake) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HandbrakeColor)}>P Applied Handbrake</color>");
			if ((alerts & HopaAlertType.AirLeak) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(AirLeakColor)}>A Air Leak</color>");
			if ((alerts & HopaAlertType.Derailment) != 0) values.Add($"<color=#{ColorUtility.ToHtmlStringRGB(DerailmentColor)}>D Derailment</color>");
			return string.Join("\n", values);
		}

		public static void Refresh(IEnumerable<Car> cars)
		{
			bool changed = false;
			SeenCarIds.Clear();
			foreach (var car in cars)
			{
				if (car == null) continue;
				int id = car.GetInstanceID();
				SeenCarIds.Add(id);
				var alerts = ReadAlerts(car);
				if (alerts == HopaAlertType.None) changed |= ActiveByCar.Remove(id);
				else if (ActiveByCar.TryGetValue(id, out var state))
				{
					int groupId = GetGroupId(car);
					int connectedCount = car.set?.Cars?.Count() ?? 1;
					string groupName = GetGroupName(car);
					string vehicleName = GetVehicleName(car);
					if (state.ActiveAlerts != alerts || state.LastGroupId != groupId || state.LastConnectedCount != connectedCount || state.LastGroupName != groupName || state.LastVehicleName != vehicleName) changed = true;
					state.Car = car;
					state.ActiveAlerts = alerts;
					state.LastGroupId = groupId;
					state.LastConnectedCount = connectedCount;
					state.LastGroupName = groupName;
					state.LastVehicleName = vehicleName;
				}
				else
				{
					ActiveByCar[id] = new HopaAlertState
					{
						Car = car,
						ActiveAlerts = alerts,
						LastGroupId = GetGroupId(car),
						LastConnectedCount = car.set?.Cars?.Count() ?? 1,
						LastGroupName = GetGroupName(car),
						LastVehicleName = GetVehicleName(car)
					};
					changed = true;
				}
			}

			RemovedCarIds.Clear();
			foreach (int id in ActiveByCar.Keys)
				if (!SeenCarIds.Contains(id)) RemovedCarIds.Add(id);
			foreach (int id in RemovedCarIds) changed |= ActiveByCar.Remove(id);
			if (changed) Changed?.Invoke();
		}

		public static void Clear()
		{
			ActiveByCar.Clear();
			SeenCarIds.Clear();
			RemovedCarIds.Clear();
			Changed?.Invoke();
		}

		private static HopaAlertType ReadAlerts(Car car)
		{
			HopaAlertType result = HopaAlertType.None;
			var settings = UMM.Loader.Settings;
			if (settings == null || !settings.ShowOperationalAlerts) return result;
			if (car.IsInBardo) return result;
			if (settings.ShowHotboxAlert && car.HasHotbox) result |= HopaAlertType.Hotbox;
			if (settings.ShowLowOilAlert && car.EnableOiling && car.Oiled < 0.25f) result |= HopaAlertType.LowOil;
			if (settings.ShowHandbrakeAlert && car.air != null && car.air.handbrakeApplied) result |= HopaAlertType.Handbrake;
			if (settings.ShowAirLeakAlert && car.air != null && (car.air.anglecockFlowA > 0.05f || car.air.anglecockFlowB > 0.05f || car.air.exhaustFlow > 0.1f)) result |= HopaAlertType.AirLeak;
			if (settings.ShowDerailmentAlert && car.IsDerailed) result |= HopaAlertType.Derailment;
			return result;
		}
	}

	/// <summary>Polls game rolling stock independently of map rendering and window lifetime.</summary>
	internal sealed class HopaAlertCollector : MonoBehaviour
	{
		private Coroutine? _poll;

		private void OnEnable()
		{
			_poll = StartCoroutine(Poll());
		}

		private IEnumerator Poll()
		{
			while (true)
			{
				var controller = TrainController.Shared;
				if (controller != null && controller.Cars != null) HopaAlertManager.Refresh(controller.Cars);
				yield return new WaitForSecondsRealtime(1f);
			}
		}

		private void OnDisable()
		{
			if (_poll != null) StopCoroutine(_poll);
			_poll = null;
			HopaAlertManager.Clear();
		}
	}

	internal sealed class HopaMapHighlight : MonoBehaviour
	{
		private Outline? _outline;
		private float _expiresAt;

		public static void Apply(GameObject marker)
		{
			var highlight = marker.GetComponent<HopaMapHighlight>();
			if (highlight == null) highlight = marker.AddComponent<HopaMapHighlight>();
			highlight._expiresAt = Time.unscaledTime + 5f;
			if (highlight._outline == null)
			{
				highlight._outline = marker.AddComponent<Outline>();
				highlight._outline.effectColor = new Color(1f, 0.92f, 0.2f, 1f);
				highlight._outline.effectDistance = new Vector2(6f, 6f);
			}
		}

		private void Update()
		{
			if (Time.unscaledTime >= _expiresAt) Destroy(this);
		}

		private void OnDestroy()
		{
			if (_outline != null) Destroy(_outline);
		}
	}
}
