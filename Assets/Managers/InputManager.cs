using System;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    public const int SupportedPlayerCount = 2;

    [Header("Input Mode")]
    [Tooltip("Enable two keyboard layouts instead of opening the ESP32 serial controllers.")]
    public bool isDebugMode = false;

    [Header("ESP32 Controllers")]
    [SerializeField] private SerialControllerConfiguration[] serialControllers =
    {
        new SerialControllerConfiguration(0, "P1"),
        new SerialControllerConfiguration(1, "P2")
    };

    [Header("Player 1 Legacy Monitor")]
    [Tooltip("Player 1 steering value. Kept for the existing title/result input flow.")]
    public float handle;
    [Tooltip("Player 1 pedal value. Kept for the existing title/result input flow.")]
    public float peddale;

    private readonly IDriveInputSource[] inputSources =
        new IDriveInputSource[SupportedPlayerCount];
    private bool initialized;

    public void Init()
    {
        DisposeInputSources();
        EnsureControllerConfigurations();

        if (isDebugMode)
        {
            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
            {
                inputSources[playerIndex] = new KeyboardDriveInputSource(playerIndex);
            }

            initialized = true;
            Debug.Log(
                "InputManager is in debug mode. " +
                "Player 1 uses WASD/Space/Enter and Player 2 uses arrow keys/Right Ctrl/Right Shift.");
            return;
        }

        string[] resolvedPorts = ResolveSerialPorts();
        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            SerialControllerConfiguration configuration = serialControllers[playerIndex];
            inputSources[playerIndex] = new SerialDriveInputSource(
                configuration,
                resolvedPorts[playerIndex]);
        }

        initialized = true;
    }

    public void UpdateInput(float deltaTime)
    {
        if (!initialized)
        {
            return;
        }

        foreach (IDriveInputSource source in inputSources)
        {
            source?.UpdateInput(deltaTime);
        }

        DriveInputState playerOneState = GetInputState(0);
        handle = playerOneState.steering;
        peddale = playerOneState.pedal;
    }

    public IDriveInputSource GetPlayerInputSource(int playerIndex)
    {
        return IsValidPlayerIndex(playerIndex) ? inputSources[playerIndex] : null;
    }

    public DriveInputState GetInputState(int playerIndex)
    {
        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null ? source.CurrentState : DriveInputState.Neutral;
    }

    public bool IsPlayerConnected(int playerIndex)
    {
        IDriveInputSource source = GetPlayerInputSource(playerIndex);
        return source != null && source.IsConnected;
    }

    private string[] ResolveSerialPorts()
    {
        string[] resolvedPorts = new string[SupportedPlayerCount];
        HashSet<string> claimedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            string configuredPort = serialControllers[playerIndex].PortName?.Trim();
            if (string.IsNullOrEmpty(configuredPort))
            {
                continue;
            }

            if (!claimedPorts.Add(configuredPort))
            {
                Debug.LogError(
                    $"Serial port {configuredPort} is assigned to more than one player. " +
                    $"Player {playerIndex + 1} will remain disconnected.");
                continue;
            }

            resolvedPorts[playerIndex] = configuredPort;
        }

        string[] availablePorts;
        try
        {
            availablePorts = SerialPort.GetPortNames();
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to enumerate serial ports: {exception.Message}");
            return resolvedPorts;
        }

        Array.Sort(availablePorts, StringComparer.OrdinalIgnoreCase);

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            if (!string.IsNullOrEmpty(resolvedPorts[playerIndex]))
            {
                continue;
            }

            List<string> candidates = new List<string>();
            foreach (string availablePort in availablePorts)
            {
                if (!claimedPorts.Contains(availablePort))
                {
                    candidates.Add(availablePort);
                }
            }

            SerialControllerConfiguration configuration = serialControllers[playerIndex];
            string discoveredPort = SerialControllerDiscovery.FindPortForDevice(
                configuration,
                candidates.ToArray());

            if (string.IsNullOrEmpty(discoveredPort))
            {
                Debug.LogError(
                    $"Could not find ESP32 controller {configuration.DeviceId} for " +
                    $"Player {playerIndex + 1}. Configure its COM port explicitly or make " +
                    "the firmware answer IDENTIFY with DEVICE,<id>.");
                continue;
            }

            resolvedPorts[playerIndex] = discoveredPort;
            claimedPorts.Add(discoveredPort);
        }

        return resolvedPorts;
    }

    private void EnsureControllerConfigurations()
    {
        if (serialControllers == null || serialControllers.Length != SupportedPlayerCount)
        {
            SerialControllerConfiguration[] previous = serialControllers;
            serialControllers = new SerialControllerConfiguration[SupportedPlayerCount];

            for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
            {
                SerialControllerConfiguration matchingConfiguration = null;
                if (previous != null)
                {
                    foreach (SerialControllerConfiguration configuration in previous)
                    {
                        if (configuration != null && configuration.PlayerIndex == playerIndex)
                        {
                            matchingConfiguration = configuration;
                            break;
                        }
                    }
                }

                serialControllers[playerIndex] = matchingConfiguration;
            }
        }

        for (int playerIndex = 0; playerIndex < SupportedPlayerCount; playerIndex++)
        {
            serialControllers[playerIndex] ??=
                new SerialControllerConfiguration(playerIndex, $"P{playerIndex + 1}");
            serialControllers[playerIndex].AssignPlayerIndex(playerIndex);
        }
    }

    private static bool IsValidPlayerIndex(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < SupportedPlayerCount;
    }

    private void DisposeInputSources()
    {
        for (int index = 0; index < inputSources.Length; index++)
        {
            inputSources[index]?.Dispose();
            inputSources[index] = null;
        }

        initialized = false;
        handle = 0f;
        peddale = 0f;
    }

    private void OnApplicationQuit()
    {
        DisposeInputSources();
    }

    private void OnDestroy()
    {
        DisposeInputSources();
    }
}
