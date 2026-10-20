using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class FinishCarVisibilityValidation
{
    public static void Run()
    {
        var previous = Gmanager.Control;
        var root = new GameObject("Finish visibility validation"); root.SetActive(false);
        try
        {
            var game = root.AddComponent<Gmanager>(); Gmanager.Control = game; game.state = Gmanager.State.Game;
            var features = root.AddComponent<RaceFeatureController>(); features.Configure(game);
            typeof(Gmanager).GetField("<Features>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, features);
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube); car.transform.SetParent(root.transform);
            var body = car.AddComponent<Rigidbody>(); car.AddComponent<DebugMover>();
            var hidden = GameObject.CreatePrimitive(PrimitiveType.Cube); hidden.transform.SetParent(car.transform);
            var hiddenMesh = hidden.GetComponent<Renderer>(); hiddenMesh.enabled = false;
            var entry = new RaceParticipant { Index = 2, IsCpu = true, Car = car, Body = body,
                Result = new RaceResultRecord { didFinish = true, totalRaceTime = 123, finishPosition = 1 } };
            ((List<RaceParticipant>)game.Participants).Add(entry);
            var run = car.AddComponent<FinishRunController>(); run.Begin(game, entry);
            Require(car.GetComponent<Renderer>().enabled && run.IsActive, "The winning run must remain visible.");
            run.Stop();
            Require(run.IsRetiredFromView && !car.GetComponent<Renderer>().enabled && !hiddenMesh.enabled,
                "A parked finisher and its children must retire from the race view.");
            features.SetFeature(RaceFeature.FinishRun, false);
            Require(!run.IsRetiredFromView && car.GetComponent<Renderer>().enabled && !hiddenMesh.enabled,
                "Live OFF must restore each renderer's original visibility.");
            features.SetFeature(RaceFeature.FinishRun, true);
            Require(run.IsRetiredFromView && !car.GetComponent<Renderer>().enabled, "Live ON must retire an already parked finisher again.");
            Require(entry.Result.totalRaceTime == 123 && entry.Result.finishPosition == 1 && body.isKinematic,
                "Visibility must not modify finish results or resume motion.");
            Debug.Log("FINISH_CAR_VISIBILITY_PASS: visible winning run, parked meshes/labels retire, live OFF/ON, original disabled child preserved, official result unchanged.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; }
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
