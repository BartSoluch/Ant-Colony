using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Range(0f, 1f)] public float scoutRatio = 0.1f;
    public int maxAnts = 100;
    public GameObject antPrefab;

    private List<AntAgent> allAnts = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        RebalanceRoles();
        TrySpawnAnt();
    }

    public void RegisterAnt(AntAgent ant)
    {
        allAnts.Add(ant);
    }

    public void NotifyAntDeath(AntAgent deadAnt)
    {
        allAnts.Remove(deadAnt);
    }

    void RebalanceRoles()
    {
        int total = allAnts.Count;
        if (total == 0) return;

        int desiredScouts = Mathf.RoundToInt(total * scoutRatio);
        int currentScouts = allAnts.FindAll(a => a.currentRole == AntAgent.Role.Scout).Count;

        if (currentScouts < desiredScouts)
        {
            var workersToSwitch = allAnts.FindAll(a => a.currentRole == AntAgent.Role.Worker);
            int needed = desiredScouts - currentScouts;

            for (int i = 0; i < needed && i < workersToSwitch.Count; i++)
                workersToSwitch[i].currentRole = AntAgent.Role.Scout;
        }
    }

    void TrySpawnAnt()
    {
        if (allAnts.Count >= maxAnts) return;

        // Spawn logic goes here (e.g., near nest center)
    }
    public bool CanSpawnMoreAnts()
    {
        return allAnts.Count < maxAnts;
    }

    public void SpawnAntAt(Vector3 position)
    {
        GameObject ant = Instantiate(antPrefab, position, Quaternion.identity);
        AntAgent agent = ant.GetComponent<AntAgent>();
        agent.currentRole = Random.value < scoutRatio ? AntAgent.Role.Scout : AntAgent.Role.Worker;
        RegisterAnt(agent);
    }

}
