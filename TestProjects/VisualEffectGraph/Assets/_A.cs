using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class _A : MonoBehaviour
{
    void Update()
    {
        var transform = GetComponent<Transform>();
        var position = transform.localPosition;
        position.x = Mathf.Sin(Time.realtimeSinceStartup * 5.0f) / Mathf.PI;
        transform.localPosition = position;
    }
}
