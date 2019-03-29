using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.VFX;

public class _CameraScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    public VisualEffectAsset m_refVFX;

    static readonly float  k_Instancitation_Period = 0.5f;
    static readonly int k_Center = Shader.PropertyToID("center");
    static readonly int k_Color = Shader.PropertyToID("color");

    private float m_nextInstanciation = k_Instancitation_Period;
    void Update()
    {
        var transform = GetComponent<Transform>();
        transform.Translate(new Vector3(0, 0, Time.deltaTime), Space.World);

        m_nextInstanciation -= Time.deltaTime;
        if (m_nextInstanciation < 0.0f)
        {
            m_nextInstanciation = k_Instancitation_Period;

            var newGameObject = new GameObject("_To_Delete", typeof(VisualEffect));
            var vfx = newGameObject.GetComponent<VisualEffect>();

            vfx.visualEffectAsset = m_refVFX;
            var center = transform.forward * 20.0f + transform.position;
            vfx.SetVector3(k_Center, center);
            var color = Color.HSVToRGB(Random.Range(0.0f, 1.0f), 1.0f, 1.0f);
            vfx.SetVector4(k_Color, color);

            //Add some environment
            var a = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Transform>();
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Transform>();
            a.localScale = b.localScale = Vector3.one * 0.25f;
            a.SetPositionAndRotation(center - new Vector3(2, 0, 0), Quaternion.identity);
            b.SetPositionAndRotation(center + new Vector3(2, 0, 0), Quaternion.identity);

        }
    }
}
