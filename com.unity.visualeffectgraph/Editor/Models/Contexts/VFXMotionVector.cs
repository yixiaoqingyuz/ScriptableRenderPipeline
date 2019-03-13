using System.Collections.Generic;
using UnityEditor.Experimental.VFX;
using UnityEngine;
using UnityEngine.Experimental.VFX;

namespace UnityEditor.VFX
{
    class VFXMotionVector : VFXContext
    {
        public VFXMotionVector() : base(VFXContextType.kUpdate, VFXDataType.kParticle, VFXDataType.kParticle) { }
        public override string name => "MotionVector";

        private VFXContext m_encapsulatedOutput;
        public VFXContext encapsulatedOutput => m_encapsulatedOutput;
        public void SetEncapsulatedOutput(VFXContext context)
        {
            m_encapsulatedOutput = context;
        }

        public override string codeGeneratorTemplate
        {
            get
            {
                return VisualEffectGraphPackageInfo.assetPackagePath + "/Shaders/VFXMotionVector";
            }
        }
        public override bool codeGeneratorCompute { get { return true; } }

        public override VFXTaskType taskType { get { return VFXTaskType.Update; } }

        public override VFXExpressionMapper GetExpressionMapper(VFXDeviceTarget target)
        {   //TODOPAUL verify which mapper really needs this
            VFXExpressionMapper mapper = m_encapsulatedOutput != null ? m_encapsulatedOutput.GetExpressionMapper(target) : null;
            if (mapper == null)
                mapper = new VFXExpressionMapper();
            if (mapper.FromNameAndId("currentFrameIndex", -1) == null)
                mapper.AddExpression(VFXBuiltInExpression.FrameIndex, "currentFrameIndex", -1);
            return mapper;
        }

        protected override IEnumerable<VFXBlock> implicitPostBlock
        {
            get
            {
                foreach (var inBase in base.implicitPostBlock)
                    yield return inBase;

                if (m_encapsulatedOutput != null)
                {
                    foreach (var block in m_encapsulatedOutput.activeChildrenWithImplicit)
                    {
                        yield return block;
                    }
                }
            }
        }

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                yield return new VFXAttributeInfo(VFXAttribute.Position, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alpha, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alive, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisZ, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleZ, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotZ, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Size, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleZ, VFXAttributeMode.Read);
            }
        }
    }
}
