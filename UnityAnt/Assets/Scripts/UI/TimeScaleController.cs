using UnityEngine;
using UnityEngine.UI;

public class TimeScaleController : MonoBehaviour
{
    [SerializeField] private Slider timeSlider;
    [SerializeField] private Text label;

    void Start()
    {
        timeSlider.onValueChanged.AddListener(UpdateTimeScale);
        UpdateTimeScale(timeSlider.value);
    }

    void UpdateTimeScale(float value)
    {
        Time.timeScale = value;
        if (label != null)
            label.text = $"Speed: {value:0.0}x";
    }
}
