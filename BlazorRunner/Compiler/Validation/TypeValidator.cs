﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlazorRunner.Runner
{
    /// <summary>
    /// Contains various helper methods that provide validation of instanced objects and their compatibility for certain features in <see cref="BlazorRunner"/>
    /// </summary>
    public static class TypeValidator
    {

        public static readonly object[] DefaultNonZeroPrimitives = {
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            0.1f,
            0.1d,
            0.1m
        };

        public static readonly object[] DefaultMaximums = {
            sbyte.MaxValue,
            byte.MaxValue,
            short.MaxValue,
            ushort.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            long.MaxValue,
            ulong.MaxValue,
            float.MaxValue,
            double.MaxValue,
            decimal.MaxValue
        };


        public const TypeCode PrimitiveTypes = TypeCode.SByte | TypeCode.Byte | TypeCode.Int16 | TypeCode.UInt16 | TypeCode.Int32 | TypeCode.UInt32 | TypeCode.Int64 | TypeCode.UInt64 | TypeCode.Single | TypeCode.Double | TypeCode.Decimal;

        public const int ImplicitSbyteConversions = (int)(BinaryTypeCode.Int16 | BinaryTypeCode.Int32 | BinaryTypeCode.Int64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.SByte);

        public const int ImplicitByteConversions = (int)(BinaryTypeCode.Int16 | BinaryTypeCode.UInt16 | BinaryTypeCode.Int32 | BinaryTypeCode.UInt32 | BinaryTypeCode.Int64 | BinaryTypeCode.UInt64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.Byte);

        public const int ImplicitShortConversions = (int)(BinaryTypeCode.Int32 | BinaryTypeCode.Int64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.Int16);

        public const int ImplicitUShortConversions = (int)(BinaryTypeCode.Int32 | BinaryTypeCode.UInt32 | BinaryTypeCode.Int64 | BinaryTypeCode.UInt64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.UInt16);

        public const int ImplicitIntConversions = (int)(BinaryTypeCode.Int64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.Int32);

        public const int ImplicitUIntConversions = (int)(BinaryTypeCode.Int64 | BinaryTypeCode.UInt64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.UInt32);

        public const int ImplicitLongConversions = (int)(BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.Int64);

        public const int ImplicitULongConversions = (int)(BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal | BinaryTypeCode.UInt64);

        public const int ImplicitFloatConversions = (int)(BinaryTypeCode.Double | BinaryTypeCode.Single);

        public const int ImplicitDoubleConversions = (int)BinaryTypeCode.Double;

        public const int ImplicitDecimalConversions = (int)BinaryTypeCode.Decimal;

        public const int ImplicitCharConversions = (int)(BinaryTypeCode.UInt16 | BinaryTypeCode.Int32 | BinaryTypeCode.UInt32 | BinaryTypeCode.Int64 | BinaryTypeCode.UInt64 | BinaryTypeCode.Single | BinaryTypeCode.Double | BinaryTypeCode.Decimal);

        public const int BoolConversions = (int)BinaryTypeCode.Boolean;

        public static readonly int[] Conversions = {
            0,
            0,
            0,
            BoolConversions,
            ImplicitCharConversions,
            ImplicitSbyteConversions,
            ImplicitByteConversions,
            ImplicitShortConversions,
            ImplicitUShortConversions,
            ImplicitIntConversions,
            ImplicitUIntConversions,
            ImplicitLongConversions,
            ImplicitULongConversions,
            ImplicitFloatConversions,
            ImplicitDoubleConversions,
            ImplicitDecimalConversions,
            0,
            0,
            0
        };

        /// <summary>
        /// Attempts to search the collection defined by <paramref name="ValidatorType"/> and if the object is an instance of any of
        /// those types, the type is returned.
        /// </summary>
        /// <param name="Instance"></param>
        /// <param name="EligibleType"></param>
        /// <param name="ValidatorType"></param>
        /// <returns></returns>
        public static bool TryGetEligibleType(object Instance, out Type EligibleType, ValidatorTypes ValidatorType)
        {
            Type instanceType = Instance.GetType();

            TypeCode instanceTypeCode = Type.GetTypeCode(instanceType);

            // left open for future extensibility, compiler should simplify it
            switch (ValidatorType)
            {
                case ValidatorTypes.EligibleSliders:
                    if ((instanceTypeCode & PrimitiveTypes) != TypeCode.Empty)
                    {
                        EligibleType = instanceType;
                        return true;
                    }
                    break;
            }

            EligibleType = null;
            return false;
        }

        public static bool TryGetCompatibility(object Instance, Type DesiredType, out CastingCompatibility Compatibility)
        {
            // default to none
            Compatibility = CastingCompatibility.none;


            if (Instance is null)
            {
                // if the instance is null and the destination is something that cant be null return false
                if (DesiredType.IsValueType)
                {
                    return false;
                }

                return true;
            }

            if (Instance.GetType() == DesiredType)
            {
                Compatibility = CastingCompatibility.SameType;
                return true;
            }

            if (IsImplicitlyCastable(Instance, DesiredType))
            {
                Compatibility = CastingCompatibility.Implicit;
                return true;
            }

            if (IsExplicitlyCastable(Instance, DesiredType))
            {
                Compatibility = CastingCompatibility.Explicit;
                return true;
            }

            if (IsExplicitlyCastable(StripNumericalSymbols(Instance.ToString()), DesiredType))
            {
                Compatibility = CastingCompatibility.Parsable;
                return true;
            }

            return false;
        }

        public static bool IsExplicitlyCastable(object Instance, Type DesiredType)
        {
            if (DesiredType.GetInterface(nameof(IConvertible)) != null)
            {
                // check to see if the instance is IConvertible
                if (Instance is IConvertible convertibleType)
                {
                    try
                    {
                        Convert.ChangeType(convertibleType, DesiredType);

                        return true;
                    }
                    catch (InvalidCastException) { }
                    catch (FormatException) { }
                    catch (OverflowException) { }
                    catch (ArgumentNullException) { }


                    return false;
                }
            }

            return false;
        }

        public static object Cast(object Instance, Type DesiredType, CastingCompatibility compatibility)
        {
            switch (compatibility)
            {
                case CastingCompatibility.SameType:
                case CastingCompatibility.Implicit:
                    return Instance;
                case CastingCompatibility.Explicit:
                    return Convert.ChangeType(Instance, DesiredType);
                case CastingCompatibility.Parsable:
                    return Convert.ChangeType(StripNumericalSymbols(Instance.ToString()), DesiredType);
                default:
                    return Instance;
            }
        }

        public static string StripNumericalSymbols(object Instance)
        {
            string s = Instance.ToString();
            s = s.Replace("f", "");
            s = s.Replace("d", "");
            s = s.Replace("m", "");
            return s;
        }

        public static bool IsImplicitlyCastable(object Instance, Type DesiredType)
        {
            if (Instance is null)
            {
                return false;
            }

            // get the typecode of the instance
            int instanceTypeCode = (int)Type.GetTypeCode(Instance.GetType());

            // get the typecode of the desired type
            int desiredTypeCode = (int)Type.GetTypeCode(DesiredType);

            // convert system typecode to binary so we can do bit math to determine implicit casting
            int desiredBinaryCode = 1 << (desiredTypeCode - 1);

            // im so sorry for this
            return (desiredBinaryCode & Conversions[instanceTypeCode]) != 0;

        }
    }
}
