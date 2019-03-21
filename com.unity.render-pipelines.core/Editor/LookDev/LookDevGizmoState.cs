using System;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace UnityEditor.Rendering.LookDev
{
    [Serializable]
    internal class LookDevGizmoState
    {
        [SerializeField]
        private Vector2 m_Point1;
        [SerializeField]
        private Vector2 m_Point2;
        [SerializeField]
        private Vector2 m_Center = Vector2.zero;
        [SerializeField]
        private float m_Angle = 0.0f;
        [SerializeField]
        private float m_Length = 0.2f;
        [SerializeField]
        private Vector4 m_Plane;
        [SerializeField]
        private Vector4 m_PlaneOrtho;
        
        public Vector2 point1 => m_Point1;
        public Vector2 point2 => m_Point2;
        public Vector2 center => m_Center;
        public float angle => m_Angle;
        public float length => m_Length;
        public Vector4 plane => m_Plane;
        public Vector4 planeOrtho => m_PlaneOrtho;

        public LookDevGizmoState()
            => Update(m_Center, m_Length, m_Angle);
        
        private Vector4 Get2DPlane(Vector2 firstPoint, float angle)
        {
            Vector4 result = new Vector4();
            angle = angle % (2.0f * (float)Math.PI);
            Vector2 secondPoint = new Vector2(firstPoint.x + Mathf.Sin(angle), firstPoint.y + Mathf.Cos(angle));
            Vector2 diff = secondPoint - firstPoint;
            if (Mathf.Abs(diff.x) < 1e-5)
            {
                result.Set(-1.0f, 0.0f, firstPoint.x, 0.0f);
                float sign = Mathf.Cos(angle) > 0.0f ? 1.0f : -1.0f;
                result *= sign;
            }
            else
            {
                float slope = diff.y / diff.x;
                result.Set(-slope, 1.0f, -(firstPoint.y - slope * firstPoint.x), 0.0f);
            }

            if (angle > Mathf.PI)
                result = -result;

            float length = Mathf.Sqrt(result.x * result.x + result.y * result.y);
            result = result / length;
            return result;
        }

        public void Update(Vector2 point1, Vector2 point2)
        {
            m_Point1 = point1;
            m_Point2 = point2;
            m_Center = (point1 + point2) * 0.5f;
            m_Length = (point2 - point1).magnitude * 0.5f;

            Vector3 verticalPlane = Get2DPlane(m_Center, 0.0f);
            float side = Vector3.Dot(new Vector3(point1.x, point1.y, 1.0f), verticalPlane);
            m_Angle = (Mathf.Deg2Rad * Vector2.Angle(new Vector2(0.0f, 1.0f), (point1 - point2).normalized));
            if (side > 0.0f)
                m_Angle = 2.0f * Mathf.PI - m_Angle;

            m_Plane = Get2DPlane(m_Center, m_Angle);
            m_PlaneOrtho = Get2DPlane(m_Center, m_Angle + 0.5f * (float)Mathf.PI);
        }

        public void Update(Vector2 center, float length, float angle)
        {
            m_Center = center;
            m_Length = length;
            m_Angle = angle;

            m_Plane = Get2DPlane(m_Center, m_Angle);
            m_PlaneOrtho = Get2DPlane(m_Center, m_Angle + 0.5f * (float)Mathf.PI);

            Vector2 dir = new Vector2(m_PlaneOrtho.x, m_PlaneOrtho.y);
            m_Point1 = m_Center + dir * m_Length;
            m_Point2 = m_Center - dir * m_Length;
        }
    }
}
