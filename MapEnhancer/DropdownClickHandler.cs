using Model;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MapEnhancer
{
	internal class DropdownClickHandler : MonoBehaviour, IPointerDownHandler
	{
		public List<Car> cars;
		private TMP_Dropdown dropdown;

		public void Awake()
		{
			dropdown = GetComponent<TMP_Dropdown>();
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (dropdown.IsExpanded) return;
			dropdown.ClearOptions();
			cars = TrainController.Shared.Cars.Where((Car car) => car.IsLocomotive).OrderBy(car => car.SortName).ToList();
			dropdown.AddOptions(cars.Select(car => car.DisplayName).Prepend("Locomotive...").ToList());
		}
	}
}
