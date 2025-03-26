using UnityEngine;

public class VoxelDigTester : MonoBehaviour
{
    public VoxelTerrainGenerator terrain;

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && terrain != null)
        {
            Vector3 digPosition = Camera.main.transform.position + Camera.main.transform.forward * 5f;
            terrain.Dig(digPosition, 2f);
        }
    }
}
