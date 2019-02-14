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
        position.y = 0.5f;
        //position.x = position.y = 0;
        position.z = 0.0f;

        //transform.localScale = new Vector3(30000.0f, 30000.0f, 1.0f) * 0.0f;

        transform.localPosition = position;
    }
}
