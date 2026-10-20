using System;
using UnityEngine;

/// <summary>Game vehicle grades and their existing mesh assets; ratings are game balance values.</summary>
[CreateAssetMenu(menuName = "Racing/Car Selection Catalog")]
public sealed class CarSelectionCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Vehicle
    {
        public string id, displayName, grade, bodyType, description;
        public GameObject model;
        public bool replaceRaceBody;
        [Range(.7f, 1.3f)] public float acceleration = 1f, topSpeed = 1f, handling = 1f, drift = 1f;
        public float SpeedRating => Mathf.Clamp(8f * topSpeed, 0f, 10f);
        public float AccelerationRating => Mathf.Clamp(8f * acceleration, 0f, 10f);
        public float HandlingRating => Mathf.Clamp(8f * handling, 0f, 10f);
        public float DriftRating => Mathf.Clamp(8f * drift, 0f, 10f);
    }

    public GameObject wheelModel;
    public Vehicle[] vehicles = Array.Empty<Vehicle>();
    public static readonly Color[] Paints = {
        new Color(.88f, .92f, 1f), new Color(.38f, .43f, .52f),
        new Color(.035f, .28f, 1f), new Color(1f, .055f, .34f), new Color(.018f, .025f, .04f)
    };
    public static readonly string[] PaintNames = { "パールホワイト", "メタリックシルバー", "サーキットブルー", "ネオンピンク", "ミッドナイトブラック" };
    public static CarSelectionCatalog Load() => Resources.Load<CarSelectionCatalog>("UI/CarSelectionCatalog");

    public void Apply(GameObject car, int vehicleIndex, int paintIndex)
    {
        Vehicle vehicle = vehicles[Mathf.Clamp(vehicleIndex, 0, vehicles.Length - 1)];
        CarSelectionModel.RestoreRaceBody(car);
        if (vehicle.replaceRaceBody) CarSelectionModel.ReplaceRaceBody(car, vehicle.model);
        PlayerCarPaint paint = car.GetComponent<PlayerCarPaint>() ?? car.AddComponent<PlayerCarPaint>();
        paint.SetColor(Paints[Mathf.Clamp(paintIndex, 0, Paints.Length - 1)]);
        car.GetComponent<DebugMover>()?.ConfigureVehicle(vehicle.acceleration, vehicle.topSpeed, vehicle.handling, vehicle.drift);
        car.GetComponent<CarSoundController>()?.ConfigureVehicleProfile(Mathf.Clamp(vehicleIndex, 0, 2));
        CarExhaustVisual exhaust = car.GetComponent<CarExhaustVisual>() ?? car.AddComponent<CarExhaustVisual>();
        exhaust.Configure(Mathf.Clamp(vehicleIndex, 0, 2));
    }
}
