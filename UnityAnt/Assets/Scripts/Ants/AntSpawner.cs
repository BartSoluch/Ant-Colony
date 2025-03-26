using UnityEngine;

public class AntSpawner : MonoBehaviour
{
    public GameObject antPrefab;
    public int numberOfAnts = 10;

    void Start()
    {
        Vector3 center = VoxelWorld.Instance.GetCenterWorldPosition();

        for (int i = 0; i < numberOfAnts; i++)
        {
            Vector3 spawnPos = center + Random.insideUnitSphere * 2f;
            spawnPos.y = center.y;
            Instantiate(antPrefab, spawnPos, Quaternion.identity);
        }
    }

}
