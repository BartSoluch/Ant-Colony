// === GameManager.cs (Fully Realistic, No Role Switching) ===
using UnityEngine;
using System.Collections.Generic;
using MarchingCubesGPUProject;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Ant Colony Settings")]
    [Range(0f, 1f)] public float scoutRatio = 0.1f;
    public int baseMaxAnts = 50;
    public float antsPerCubicMeter = 0.5f;
    public GameObject antPrefab;
    public static bool colonyDiggingStarted = false;

    private List<AntAgent> allAnts = new();
    private float dugVolume = 0f;
    private int digCounter = 0;
    public int digsPerColonyGrowth = 20;
    public float colonyGrowthPerBatch = 2f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update() 
    { 
        
    }

    public void RegisterAnt(AntAgent ant)
    {
        allAnts.Add(ant);
    }

    public void NotifyAntDeath(AntAgent deadAnt)
    {
        allAnts.Remove(deadAnt);
    }

    public bool CanSpawnMoreAnts()
    {
        return allAnts.Count < GetMaxAnts();
    }

    public void RegisterDigEvent()
    {
        digCounter++;

        if (digCounter >= digsPerColonyGrowth)
        {
            digCounter = 0;
            dugVolume += colonyGrowthPerBatch;
        }
    }

    public int GetMaxAnts()
    {
        return baseMaxAnts + Mathf.RoundToInt((dugVolume/20) * antsPerCubicMeter);
    }

    public float GetDugVolume()
    {
        return dugVolume;
    }

    public int GetCurrentAntCount()
    {
        return allAnts.Count;
    }

    public bool ShouldQueenLayEgg()
    {
        float idealAnts = dugVolume * antsPerCubicMeter;
        float pressure = Mathf.Clamp01((idealAnts - GetCurrentAntCount()) / Mathf.Max(idealAnts, 1f));
        float randomFactor = Random.Range(0f, 1f);
        return (pressure + randomFactor * 0.5f) > 0.5f;
    }

    public AntAgent.Role DecideNextAntRole()
    {
        int workers = allAnts.FindAll(a => a.currentRole == AntAgent.Role.Worker).Count;
        int scouts = allAnts.FindAll(a => a.currentRole == AntAgent.Role.Scout).Count;

        if (workers < (allAnts.Count * 1f))
            return AntAgent.Role.Worker;
        else
            return AntAgent.Role.Scout;
    }

    public void SpawnAntAt(Vector3 position, AntAgent.Role role)
    {
        GameObject ant = Instantiate(antPrefab, position, Quaternion.identity);
        AntAgent agent = ant.GetComponent<AntAgent>();
        agent.currentRole = role;
        RegisterAnt(agent);
    }
}