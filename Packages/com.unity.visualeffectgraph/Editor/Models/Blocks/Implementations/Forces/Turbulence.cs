using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;


namespace UnityEditor.VFX.Block
{
    [VFXInfo(category = "Force")]
    class Turbulence : VFXBlock
    {
        public class InputProperties
        {
            [Tooltip("The position, rotation and scale of the turbulence field")]
            public Transform FieldTransform = Transform.defaultValue;
            [Tooltip("Intensity of the motion vectors")]
            public float Intensity = 1.0f;
        }

        [VFXSetting, SerializeField]
        ForceMode Mode = ForceMode.Relative;
        [VFXSetting, SerializeField]
        VFX.Operator.NoiseBase.NoiseType NoiseType = VFX.Operator.NoiseBase.NoiseType.Value;


        public override string name { get { return "Turbulence"; } }
        public override VFXContextType compatibleContexts { get { return VFXContextType.Update; } }
        public override VFXDataType compatibleData { get { return VFXDataType.Particle; } }
        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                yield return new VFXAttributeInfo(VFXAttribute.Position, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Velocity, VFXAttributeMode.ReadWrite);
                yield return new VFXAttributeInfo(VFXAttribute.Mass, VFXAttributeMode.Read);
            }
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                var properties = base.inputProperties;
                if (Mode == ForceMode.Relative)
                    properties = properties.Concat(PropertiesFromType(typeof(ForceHelper.DragProperties)));
                properties = properties.Concat(PropertiesFromType(typeof(VFX.Operator.NoiseBase.InputPropertiesCommon)));
                return properties;
            }
        }

        public override IEnumerable<VFXNamedExpression> parameters
        {
            get
            {
                foreach (var input in GetExpressionsFromSlots(this))
                {
                    if (input.name == "octaves") continue;

                    if (input.name == "FieldTransform")
                        yield return new VFXNamedExpression(new VFXExpressionInverseTRSMatrix(input.exp), "InvFieldTransform");
                    yield return input;
                }

                // Clamp (1..10) for octaves (TODO: Add a Range attribute that works with int instead of doing that
                yield return new VFXNamedExpression(new VFXExpressionCastFloatToInt(VFXOperatorUtility.Clamp(new VFXExpressionCastIntToFloat(inputSlots[4].GetExpression()), VFXValue.Constant(1.0f), VFXValue.Constant(10.0f))), "octaves");
                yield return new VFXNamedExpression(VFXBuiltInExpression.DeltaTime, "deltaTime");
            }
        }

        public override string source
        {
            get
            {
                return 
 $@"float3 vectorFieldCoord = mul(InvFieldTransform, float4(position,1.0f)).xyz;

float3 value = Generate{NoiseType.ToString()}CurlNoise(vectorFieldCoord + 0.5f, frequency, octaves, roughness, lacunarity);
value = mul(FieldTransform,float4(value,0.0f)).xyz * Intensity;

velocity += {ForceHelper.ApplyForceString(Mode, "value")};";
            }
        }

        public override void Sanitize(int version)
        {
            var oldRoughness = inputSlots.FirstOrDefault(s => s.name == "Roughness");
            base.Sanitize(version);
            if (oldRoughness != default(VFXSlot))
            {
                var newRoughness = inputSlots.FirstOrDefault(s => s.name == "roughness");
                if (newRoughness != default(VFXSlot))
                    VFXSlot.CopyLinksAndValue(newRoughness, oldRoughness, true);
            }
        }
    }
}
