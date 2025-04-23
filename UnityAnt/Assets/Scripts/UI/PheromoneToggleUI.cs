using UnityEngine;
using UnityEngine.UI;

public class PheromoneToggleUI : MonoBehaviour
{
    public Toggle visualsToggle;

    void Start()
    {
        visualsToggle.onValueChanged.AddListener(isOn =>
        {
            if (PheromoneField.Instance != null)
                PheromoneField.Instance.SetVisualsEnabled(isOn);
        });

        if (PheromoneField.Instance != null)
            PheromoneField.Instance.SetVisualsEnabled(
                visualsToggle.isOn
            );
    }
}
