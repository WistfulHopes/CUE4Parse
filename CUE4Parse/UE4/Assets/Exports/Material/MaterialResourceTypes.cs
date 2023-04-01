using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using Serilog;

namespace CUE4Parse.UE4.Assets.Exports.Material
{
    public class FMaterialResource : FMaterial
    {
    }

    public class FMaterial
    {
        public FMaterialShaderMap LoadedShaderMap;
        public FMaterialShaderMapOld LoadedShaderMapOld;

        public void DeserializeInlineShaderMap(FMaterialResourceProxyReader Ar)
        {
            var bCooked = Ar.ReadBoolean();
            if (!bCooked) return;

            var bValid = Ar.ReadBoolean();
            if (bValid)
            {
                LoadedShaderMap = new FMaterialShaderMap();
                LoadedShaderMap.Deserialize(Ar);
            }
            else
            {
                Log.Warning("Loading a material resource '{0}' with an invalid ShaderMap!", Ar.Name);
            }
        }
        public void DeserializeInlineShaderMapOld(FMaterialResourceProxyReader Ar)
        {
            var bCooked = Ar.ReadBoolean();
            if (!bCooked) return;

            var bValid = Ar.ReadBoolean();
            if (bValid)
            {
                LoadedShaderMapOld = new FMaterialShaderMapOld();
                LoadedShaderMapOld.Deserialize(Ar);
            }
            else
            {
                Log.Warning("Loading a material resource '{0}' with an invalid ShaderMap!", Ar.Name);
            }
        }
    }

    public abstract class FShaderMapBase
    {
        public FShaderMapContent Content;
        public FSHAHash ResourceHash;
        public FShaderMapResourceCode Code;

        public void Deserialize(FMaterialResourceProxyReader Ar)
        {
            var bUseNewFormat = Ar.Versions["ShaderMap.UseNewCookedFormat"];

            var pointerTable = new FShaderMapPointerTable();
            var result = new FMemoryImageResult(pointerTable);
            result.LoadFromArchive(Ar);
            Content = ReadContent(
                new FMemoryImageArchive(new FByteArchive("FShaderMapContent", result.FrozenObject, Ar.Versions))
                {
                    Names = result.GetNames()
                });

            var bShareCode = Ar.ReadBoolean();
            if (bUseNewFormat)
            {
                var shaderPlatform = Ar.Game >= EGame.GAME_UE5_2
                    ? Ar.ReadFString()
                    : Ar.Read<EShaderPlatform>().ToString();
            }

            if (bShareCode)
            {
                ResourceHash = new FSHAHash(Ar);
            }
            else
            {
                Code = new FShaderMapResourceCode(Ar);
            }
        }

        protected abstract FShaderMapContent ReadContent(FMemoryImageArchive Ar);
    }

    public class FShaderMapContent
    {
        public int[] ShaderHash;
        public FHashedName[] ShaderTypes;
        public int[] ShaderPermutations;
        public FShader[] Shaders;
        public FShaderPipeline[] ShaderPipelines;
        public EShaderPlatform ShaderPlatform;

        public FShaderMapContent(FMemoryImageArchive Ar)
        {
            ShaderHash = Ar.ReadHashTable();
            ShaderTypes = Ar.ReadArray<FHashedName>();
            ShaderPermutations = Ar.ReadArray<int>();
            Shaders = Ar.ReadArrayOfPtrs(() => new FShader(Ar));
            ShaderPipelines = Ar.ReadArrayOfPtrs(() => new FShaderPipeline(Ar));
            if (Ar.Game >= EGame.GAME_UE5_2)
            {
                var shaderPlatform = Ar.ReadFString();
                Enum.TryParse("SP_" + shaderPlatform, out ShaderPlatform);
            }
            else
            {
                ShaderPlatform = Ar.Read<EShaderPlatform>();
                Ar.Position += 7;
            }
        }
    }

    public class FShaderPipeline
    {
        private const int SF_NumGraphicsFrequencies = 5;
        public enum EFilter
        {
            EAll,			// All pipelines
            EOnlyShared,	// Only pipelines with shared shaders
            EOnlyUnique,	// Only pipelines with unique shaders
        }

        public FHashedName TypeName;
        public FShader[] Shaders;
        public int[] PermutationIds;

        public FShaderPipeline(FMemoryImageArchive Ar)
        {
            TypeName = new FHashedName(Ar);
            Shaders = new FShader[SF_NumGraphicsFrequencies];
            for (int i = 0; i < Shaders.Length; i++)
            {
                var entryPtrPos = Ar.Position;
                var entryPtr = new FFrozenMemoryImagePtr(Ar);
                if (entryPtr.IsFrozen)
                {
                    Ar.Position = entryPtrPos + entryPtr.OffsetFromThis;
                    Shaders[i] = new FShader(Ar);
                }
                Ar.Position = (entryPtrPos + 8).Align(8);
            }
            PermutationIds = Ar.ReadArray<int>(SF_NumGraphicsFrequencies);
        }
    }

    public class FShaderResource
    {
        public FName SpecificType;
        public int SpecificPermutationId;
        public FShaderTargetOld Target;
        public byte[] Code;
        public FSHAHash OutputHash;
        public uint NumInstructions;
        public FShaderParameterMapInfoOld ParameterMapInfo;
        public uint NumTextureSamplers;
        public int UncompressedSize;
        public bool bCodeShared;
        public FShaderResource(FArchive Ar)
        {
            SpecificType = Ar.ReadFName();
            if (Ar.CustomVer(FRenderingObjectVersion.GUID) >= (int) FRenderingObjectVersion.Type.ShaderPermutationId)
            {
                SpecificPermutationId = Ar.Read<int>();
            }
            Target = Ar.Read<FShaderTargetOld>();

            OutputHash = new FSHAHash(Ar);
            NumInstructions = Ar.Read<uint>();
            if (Ar.Ver >= EUnrealEngineObjectUE4Version.COMPRESSED_SHADER_RESOURCES)
            {
                UncompressedSize = Ar.Read<int>();
            }
            if (Ar.CustomVer(FRenderingObjectVersion.GUID) < (int) FRenderingObjectVersion.Type.ShaderResourceCodeSharing)
            {
                Code = Ar.ReadArray<byte>();
            }

            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                ParameterMapInfo = new FShaderParameterMapInfoOld(Ar);
            }
            else
            {
                NumTextureSamplers = Ar.Read<uint>();
            }

            if (Ar.CustomVer(FRenderingObjectVersion.GUID) >=
                (int) FRenderingObjectVersion.Type.ShaderResourceCodeSharing)
            {
                bCodeShared = Ar.Read<bool>();
            }
        }
    }

    public abstract class FShaderOld
    {
        public FSHAHash OutputHash;
        public FSHAHash MaterialShaderMapHash;
        public FName ShaderPipeline;
        public FName VFType; // TIndexedPtr<FVertexFactoryType>
        public FSHAHash VFSourceHash;
        public FName Type; // TIndexedPtr<FShaderType>
        public FSHAHash SourceHash;
        public int PermutationId;
        public FShaderTargetOld Target;
        public FShaderUniformBufferParameterOld[] UniformBufferParameters;
        public FShaderResource Resource;
        public FShaderParameterBindings Bindings;

        public static FShaderOld? MatchParameterType(FName TypeName)
        {
            if (TypeName.ToString() == "FMaterialShader")
            {
                return new FMaterialShader();
            }

            Log.Warning("Loading an unknown shader type '{0}'!", TypeName);
            return null;
        }

        public abstract void Deserialize(FArchive Ar);

        public void DeserializeBase(FArchive Ar)
        {
            Deserialize(Ar);
            OutputHash = new FSHAHash(Ar);
            MaterialShaderMapHash = new FSHAHash(Ar);
            ShaderPipeline = Ar.ReadFName();
            VFType = Ar.ReadFName();
            VFSourceHash = new FSHAHash(Ar);
            Type = Ar.ReadFName();
            if (Ar.CustomVer(FRenderingObjectVersion.GUID) >= (int) FRenderingObjectVersion.Type.ShaderPermutationId)
            {
                PermutationId = Ar.Read<int>();
            }
            SourceHash = new FSHAHash(Ar);
            Target = Ar.Read<FShaderTargetOld>();
            int NumUniformParameters = Ar.Read<int>();
            UniformBufferParameters = new FShaderUniformBufferParameterOld[NumUniformParameters];
            foreach (int index in Enumerable.Range(0, NumUniformParameters))
            {
                Ar.ReadFName();
                UniformBufferParameters[index] = new FShaderUniformBufferParameterOld(Ar);
            }
            Resource = new FShaderResource(Ar);
            Bindings = new FShaderParameterBindings(Ar);
        }
    }

    public class FSceneTextureShaderParameters
    {
        public FShaderUniformBufferParameterOld SceneTexturesUniformBuffer;
        public FShaderUniformBufferParameterOld MobileSceneTexturesUniformBuffer;

        public FSceneTextureShaderParameters(FArchive Ar)
        {
            SceneTexturesUniformBuffer = new FShaderUniformBufferParameterOld(Ar);
            MobileSceneTexturesUniformBuffer = new FShaderUniformBufferParameterOld(Ar);
        }
    }

    public class FDebugUniformExpressionSet
    {
        public int NumVectorExpressions;
        public int NumScalarExpressions;
        public int Num2DTextureExpressions;
        public int NumCubeTextureExpressions;
        public int NumVolumeTextureExpressions;

        public FDebugUniformExpressionSet(FArchive Ar)
        {
            NumVectorExpressions = Ar.Read<int>();
            NumScalarExpressions = Ar.Read<int>();
            Num2DTextureExpressions = Ar.Read<int>();
            NumCubeTextureExpressions = Ar.Read<int>();
            NumVolumeTextureExpressions = Ar.Read<int>();
        }
    }

    public class FMaterialShader : FShaderOld
    {
        public FSceneTextureShaderParameters SceneTextureParameters;
        public FShaderUniformBufferParameterOld MaterialUniformBuffer;
        public FShaderUniformBufferParameterOld[] ParameterCollectionUniformBuffers;
        public FName LayoutName;
        public FDebugUniformExpressionSet DebugUniformExpressionSet;
        public string DebugDescription;
        public FShaderResourceParameter VTFeedbackBuffer;
        public FShaderResourceParameter PhysicalTexture;
        public FShaderResourceParameter PhysicalTextureSampler;
        public FShaderResourceParameter PageTable;
        public FShaderResourceParameter PageTableSampler;
        public FShaderParameter InstanceCount;
        public FShaderParameter InstanceOffset;
        public FShaderParameter VertexOffset;

        public override void Deserialize(FArchive Ar)
        {
            SceneTextureParameters = new FSceneTextureShaderParameters(Ar);
            MaterialUniformBuffer = new FShaderUniformBufferParameterOld(Ar);
            ParameterCollectionUniformBuffers = Ar.ReadArray(() => new FShaderUniformBufferParameterOld(Ar));
            LayoutName = Ar.ReadFName();
            DebugUniformExpressionSet = new FDebugUniformExpressionSet(Ar);
            DebugDescription = Ar.ReadFString();
            VTFeedbackBuffer = new FShaderResourceParameter(Ar);
            PhysicalTexture = new FShaderResourceParameter(Ar);
            PhysicalTextureSampler = new FShaderResourceParameter(Ar);
            PageTable = new FShaderResourceParameter(Ar);
            PageTableSampler = new FShaderResourceParameter(Ar);
            InstanceCount = new FShaderParameter(Ar);
            InstanceOffset = new FShaderParameter(Ar);
            VertexOffset = new FShaderParameter(Ar);
        }
    }

    public class FSurfelBufferParameters
    {
        public FRWShaderParameter InterpolatedVertexData;
        public FRWShaderParameter SurfelData;
        public FRWShaderParameter VPLFlux;

        public FSurfelBufferParameters(FArchive Ar)
        {
            InterpolatedVertexData = new FRWShaderParameter(Ar);
            SurfelData = new FRWShaderParameter(Ar);
            VPLFlux = new FRWShaderParameter(Ar);
        }
    }

    public class FEvaluateSurfelMaterialCS : FMaterialShader
    {
        public FSurfelBufferParameters SurfelBufferParameters;
        public FShaderParameter SurfelStartIndex;
        public FShaderParameter NumSurfelsToGenerate;
        public FShaderParameter Instance0InverseTransform;

        public override void Deserialize(FArchive Ar)
        {
            base.Deserialize(Ar);
            SurfelBufferParameters = new FSurfelBufferParameters(Ar);
            SurfelStartIndex = new FShaderParameter(Ar);
            NumSurfelsToGenerate = new FShaderParameter(Ar);
            Instance0InverseTransform = new FShaderParameter(Ar);
        }
    }

    public class FShader
    {
        public FShaderParameterBindings Bindings;
        public FShaderParameterMapInfo ParameterMapInfo;
        public FHashedName[] UniformBufferParameterStructs;
        public FShaderUniformBufferParameter[] UniformBufferParameters;
        public ulong Type; // TIndexedPtr<FShaderType>
        public ulong VFType; // TIndexedPtr<FVertexFactoryType>
        public FShaderTarget Target;
        public int ResourceIndex;
        public uint NumInstructions;
        public uint SortKey;

        public FShader(FMemoryImageArchive Ar)
        {
            Bindings = new FShaderParameterBindings(Ar);
            ParameterMapInfo = new FShaderParameterMapInfo(Ar);
            UniformBufferParameterStructs = Ar.ReadArray<FHashedName>();
            UniformBufferParameters = Ar.ReadArray<FShaderUniformBufferParameter>();
            Type = Ar.Read<ulong>();
            VFType = Ar.Read<ulong>();
            Target = Ar.Read<FShaderTarget>();
            ResourceIndex = Ar.Read<int>();
            NumInstructions = Ar.Read<uint>();
            SortKey = Ar.Game >= EGame.GAME_UE5_0 ? Ar.Read<uint>() : 0;
        }
    }

    public class FShaderParameterBindings
    {
        public FParameter[]? Parameters;
        public FResourceParameter[]? Textures;
        public FResourceParameter[]? SRVs;
        public FResourceParameter[]? UAVs;
        public FResourceParameter[]? Samplers;
        public FResourceParameter[]? GraphTextures;
        public FResourceParameter[]? GraphSRVs;
        public FResourceParameter[]? GraphUAVs;
        public FResourceParameter[]? ResourceParameters;
        public FBindlessResourceParameter[] BindlessResourceParameters;
        public FParameterStructReference[] GraphUniformBuffers;
        public FParameterStructReference[] ParameterReferences;

        public uint StructureLayoutHash = 0;
        public ushort RootParameterBufferIndex = 0xFFFF;


        public FShaderParameterBindings(FArchive Ar)
        {
            Parameters = Ar.ReadArray<FParameter>();
            if (Ar.Game>= EGame.GAME_UE4_26)
            {
                ResourceParameters = Ar.ReadArray<FResourceParameter>();
            }
            else
            {
                Textures = Ar.ReadArray(() => new FResourceParameter(Ar));
                SRVs = Ar.ReadArray(() => new FResourceParameter(Ar));
                UAVs = Ar.ReadArray(() => new FResourceParameter(Ar));
                Samplers = Ar.ReadArray(() => new FResourceParameter(Ar));
                GraphTextures = Ar.ReadArray(() => new FResourceParameter(Ar));
                GraphSRVs = Ar.ReadArray(() => new FResourceParameter(Ar));
                GraphUAVs = Ar.ReadArray(() => new FResourceParameter(Ar));
            }

            BindlessResourceParameters = Ar.Game >= EGame.GAME_UE5_1 ? Ar.ReadArray<FBindlessResourceParameter>() : Array.Empty<FBindlessResourceParameter>();
            GraphUniformBuffers = Ar.Game >= EGame.GAME_UE4_26 ? Ar.ReadArray<FParameterStructReference>() : Array.Empty<FParameterStructReference>();
            ParameterReferences = Ar.ReadArray<FParameterStructReference>();

            StructureLayoutHash = Ar.Read<uint>();
            RootParameterBufferIndex = Ar.Read<ushort>();
            Ar.Position += 2;
        }

        public struct FParameter
        {
            public ushort BufferIndex;
            public ushort BaseIndex;
            public ushort ByteOffset;
            public ushort ByteSize;
        }

        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct FResourceParameter
        {
            public ushort ByteOffset;
            public byte BaseIndex;
            public EUniformBufferBaseType BaseType = EUniformBufferBaseType.UBMT_INVALID;
            //4.26+
            //LAYOUT_FIELD(uint16, ByteOffset);
            //LAYOUT_FIELD(uint8, BaseIndex);
		    //LAYOUT_FIELD(EUniformBufferBaseType, BaseType);

            //4.26-
            //LAYOUT_FIELD(uint16, BaseIndex);
		    //LAYOUT_FIELD(uint16, ByteOffset);

            public FResourceParameter(FArchive Ar)
            {
                BaseIndex = (byte)Ar.Read<ushort>();
                ByteOffset = Ar.Read<ushort>();
            }

        }

        [StructLayout(LayoutKind.Sequential, Size = 8)]
        public struct FBindlessResourceParameter
        {
            public ushort ByteOffset;
            public ushort GlobalConstantOffset;
            public EUniformBufferBaseType BaseType;
        }

        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct FParameterStructReference
        {
            public ushort BufferIndex;
            public ushort ByteOffset;
        }
    }

    public class FShaderParameterMapInfoOld
    {
        public FShaderParameterInfo[] UniformBuffers;
        public FShaderParameterInfo[] TextureSamplers;
        public FShaderParameterInfo[] SRVs;
        public FShaderLooseParameterBufferInfo[] LooseParameterBuffers;

        public FShaderParameterMapInfoOld(FArchive Ar)
        {
            UniformBuffers = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
            TextureSamplers = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
            SRVs = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
            LooseParameterBuffers = Ar.ReadArray(() => new FShaderLooseParameterBufferInfo(Ar));
        }
    }

    public class FShaderParameterMapInfo
    {
        public FShaderParameterInfo[] UniformBuffers;
        public FShaderParameterInfo[] TextureSamplers;
        public FShaderParameterInfo[] SRVs;
        public FShaderLooseParameterBufferInfo[] LooseParameterBuffers;
        public ulong Hash;

        public FShaderParameterMapInfo(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE5_1)
            {
                UniformBuffers = Ar.ReadArray(() => new FShaderUniformBufferParameterInfo(Ar));
                TextureSamplers = Ar.ReadArray(() => new FShaderResourceParameterInfo(Ar));
                SRVs = Ar.ReadArray(() => new FShaderResourceParameterInfo(Ar));
            }
            else //4.25-5.0
            {
                UniformBuffers = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
                TextureSamplers = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
                SRVs = Ar.ReadArray(() => new FShaderParameterInfo(Ar));
            }
            LooseParameterBuffers = Ar.ReadArray(() => new FShaderLooseParameterBufferInfo(Ar));
            Hash = Ar.Game >= EGame.GAME_UE4_26 ? Ar.Read<ulong>() : 0;
        }
    }

    public class FShaderLooseParameterBufferInfo
    {
        public ushort BaseIndex, Size;
        public FShaderLooseParameterInfo[] Parameters;

        public FShaderLooseParameterBufferInfo(FArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
            Size = Ar.Read<ushort>();
            Ar.Position += 4;
            Parameters = Ar.ReadArray<FShaderLooseParameterInfo>();
        }
    }

    public class FShaderParameterInfo
    {
        public ushort BaseIndex;
        public ushort Size;

        public FShaderParameterInfo(FArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
            Size = Ar.Read<ushort>();
        }

        public FShaderParameterInfo() { }
    }
    public struct FShaderLooseParameterInfo
    {
        public ushort BaseIndex, Size;
    }

    public class FShaderResourceParameterInfo : FShaderParameterInfo
    {
        public byte BufferIndex;
        public byte Type; // EShaderParameterType

        public FShaderResourceParameterInfo(FMemoryImageArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
            BufferIndex = Ar.Read<byte>();
            Type = Ar.Read<byte>();
        }
    }

    public class FShaderParameter
    {
        public ushort BufferIndex;
        public ushort BaseIndex;
        public ushort NumBytes;

        public FShaderParameter(FArchive Ar)
        {
            BufferIndex = Ar.Read<ushort>();
            BaseIndex = Ar.Read<ushort>();
            NumBytes = Ar.Read<ushort>();
        }
    }

    public class FShaderResourceParameter
    {
        public ushort BaseIndex;
        public ushort NumResources;

        public FShaderResourceParameter(FArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
            NumResources = Ar.Read<ushort>();
        }
    }

    public class FRWShaderParameter
    {
        public FShaderResourceParameter SRVParameter;
        public FShaderResourceParameter UAVParameter;

        public FRWShaderParameter(FArchive Ar)
        {
            SRVParameter = new FShaderResourceParameter(Ar);
            UAVParameter = new FShaderResourceParameter(Ar);
        }
    }

    public struct FShaderUniformBufferParameterOld
    {
        public ushort BaseIndex;
        public bool bIsBound;

        public FShaderUniformBufferParameterOld(FArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
            bIsBound = Ar.Read<bool>();
        }
    }
    public struct FShaderUniformBufferParameter
    {
        public ushort BaseIndex;
    }

    public class FShaderUniformBufferParameterInfo : FShaderParameterInfo
    {
        public FShaderUniformBufferParameterInfo(FMemoryImageArchive Ar)
        {
            BaseIndex = Ar.Read<ushort>();
        }
    }

    public struct FShaderTargetOld
    {
        public uint Frequency;
        public uint Platform;

        public FShaderTargetOld(FArchive Ar)
        {
            Frequency = Ar.Read<uint>();
            Platform = Ar.Read<uint>();
        }
    }

    public struct FShaderTarget
    {
        private uint _packed;
    }

    /** The base type of a value in a shader parameter structure. */
    public enum EUniformBufferBaseType : byte
    {
        UBMT_INVALID,

        // Invalid type when trying to use bool, to have explicit error message to programmer on why
        // they shouldn't use bool in shader parameter structures.
        UBMT_BOOL,

        // Parameter types.
        UBMT_INT32,
        UBMT_UINT32,
        UBMT_FLOAT32,

        // RHI resources not tracked by render graph.
        UBMT_TEXTURE,
        UBMT_SRV,
        UBMT_UAV,
        UBMT_SAMPLER,

        // Resources tracked by render graph.
        UBMT_RDG_TEXTURE,
        UBMT_RDG_TEXTURE_ACCESS,
        UBMT_RDG_TEXTURE_ACCESS_ARRAY,
        UBMT_RDG_TEXTURE_SRV,
        UBMT_RDG_TEXTURE_UAV,
        UBMT_RDG_BUFFER_ACCESS,
        UBMT_RDG_BUFFER_ACCESS_ARRAY,
        UBMT_RDG_BUFFER_SRV,
        UBMT_RDG_BUFFER_UAV,
        UBMT_RDG_UNIFORM_BUFFER,

        // Nested structure.
        UBMT_NESTED_STRUCT,

        // Structure that is nested on C++ side, but included on shader side.
        UBMT_INCLUDED_STRUCT,

        // GPU Indirection reference of struct, like is currently named Uniform buffer.
        UBMT_REFERENCED_STRUCT,

        // Structure dedicated to setup render targets for a rasterizer pass.
        UBMT_RENDER_TARGET_BINDING_SLOTS,

        EUniformBufferBaseType_Num,
        EUniformBufferBaseType_NumBits = 5,
    }

    public class FMaterialShaderMapContent : FShaderMapContent
    {
        public FMeshMaterialShaderMap[] OrderedMeshShaderMaps;
        public FMaterialCompilationOutput MaterialCompilationOutput;
        public FSHAHash ShaderContentHash;

        public FMaterialShaderMapContent(FMemoryImageArchive Ar) : base(Ar)
        {
            OrderedMeshShaderMaps = Ar.ReadArrayOfPtrs(() => new FMeshMaterialShaderMap(Ar));
            MaterialCompilationOutput = new FMaterialCompilationOutput(Ar);
            ShaderContentHash = new FSHAHash(Ar);
        }
    }

    public class FMeshMaterialShaderMapOld : TShaderMap
    {
        public ulong VertexFactoryType;

        public new void Deserialize(FMaterialResourceProxyReader Ar)
        {
            VertexFactoryType = Ar.Read<ulong>();
            base.Deserialize(Ar);
        }
    }
    public class FMeshMaterialShaderMap : FShaderMapContent
    {
        public FHashedName VertexFactoryTypeName;

        public FMeshMaterialShaderMap(FMemoryImageArchive Ar) : base(Ar)
        {
            VertexFactoryTypeName = Ar.Read<FHashedName>();
        }
    }

    public class FMaterialCompilationOutput
    {
        public FUniformExpressionSet UniformExpressionSet;
        public uint UsedSceneTextures;
        public byte UsedDBufferTextures;
        public byte RuntimeVirtualTextureOutputAttributeMask;

        //LAYOUT_BITFIELD(uint8, bNeedsSceneTextures, 1);
        //LAYOUT_BITFIELD(uint8, bUsesEyeAdaptation, 1);
        //LAYOUT_BITFIELD(uint8, bModifiesMeshPosition, 1);
        //LAYOUT_BITFIELD(uint8, bUsesWorldPositionOffset, 1);
        //LAYOUT_BITFIELD(uint8, bUsesGlobalDistanceField, 1);
        //LAYOUT_BITFIELD(uint8, bUsesPixelDepthOffset, 1);
        //LAYOUT_BITFIELD(uint8, bUsesDistanceCullFade, 1);
        //LAYOUT_BITFIELD(uint8, bUsesPerInstanceCustomData, 1);
        public byte b1;

        //LAYOUT_BITFIELD(uint8, bUsesPerInstanceRandom, 1);
        //LAYOUT_BITFIELD(uint8, bUsesVertexInterpolator, 1);
        //LAYOUT_BITFIELD(uint8, bHasRuntimeVirtualTextureOutputNode, 1);
        //LAYOUT_BITFIELD(uint8, bUsesAnisotropy, 1);
        public byte b2;

        public FMaterialCompilationOutput(FMemoryImageArchive Ar)
        {
            UniformExpressionSet = new FUniformExpressionSet(Ar);
            UsedSceneTextures = Ar.Read<uint>();
            UsedDBufferTextures = Ar.Read<byte>();
            RuntimeVirtualTextureOutputAttributeMask = Ar.Read<byte>();
            b1 = Ar.Read<byte>();
            b2 = Ar.Read<byte>();
        }
    }

    public class FUniformExpressionSet
    {
        public FMaterialUniformPreshaderHeader[] UniformVectorPreshaders = Array.Empty<FMaterialUniformPreshaderHeader>();
        public FMaterialUniformPreshaderHeader[] UniformScalarPreshaders = Array.Empty<FMaterialUniformPreshaderHeader>();
        public FMaterialScalarParameterInfo[] UniformScalarParameters = Array.Empty<FMaterialScalarParameterInfo>();
        public FMaterialVectorParameterInfo[] UniformVectorParameters = Array.Empty<FMaterialVectorParameterInfo>();

        public FMaterialUniformPreshaderHeader[] UniformPreshaders = Array.Empty<FMaterialUniformPreshaderHeader>();
        public FMaterialUniformPreshaderField[]? UniformPreshaderFields;
        public FMaterialNumericParameterInfo[] UniformNumericParameters = Array.Empty<FMaterialNumericParameterInfo>();
        public readonly FMaterialTextureParameterInfo[][] UniformTextureParameters = new FMaterialTextureParameterInfo[6][];
        public FMaterialExternalTextureParameterInfo[] UniformExternalTextureParameters;
        public uint UniformPreshaderBufferSize;
        public FMaterialPreshaderData UniformPreshaderData;
        public byte[] DefaultValues;
        public FMaterialVirtualTextureStack[] VTStacks;
        public FGuid[] ParameterCollections;
        public FRHIUniformBufferLayoutInitializer UniformBufferLayoutInitializer;

        public FUniformExpressionSet(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE5_0)
            {
                UniformPreshaders = Ar.ReadArray(() => new FMaterialUniformPreshaderHeader(Ar));
                UniformPreshaderFields = Ar.Game >= EGame.GAME_UE5_1 ? Ar.ReadArray<FMaterialUniformPreshaderField>() : Array.Empty<FMaterialUniformPreshaderField>();
                UniformNumericParameters = Ar.ReadArray(() => new FMaterialNumericParameterInfo(Ar));
                Ar.ReadArray(UniformTextureParameters, () => Ar.ReadArray(() => new FMaterialTextureParameterInfo(Ar)));
                UniformExternalTextureParameters = Ar.ReadArray(() => new FMaterialExternalTextureParameterInfo(Ar));
                UniformPreshaderBufferSize = Ar.Read<uint>();
                Ar.Position += 4;
                UniformPreshaderData = new FMaterialPreshaderData(Ar);
                DefaultValues = Ar.ReadArray<byte>();
                VTStacks = Ar.ReadArray(() => new FMaterialVirtualTextureStack(Ar));
                ParameterCollections = Ar.ReadArray<FGuid>();
                UniformBufferLayoutInitializer = new FRHIUniformBufferLayoutInitializer(Ar);
            }
            else
            {
                UniformVectorPreshaders = Ar.ReadArray(() => new FMaterialUniformPreshaderHeader(Ar));
                UniformScalarPreshaders = Ar.ReadArray(() => new FMaterialUniformPreshaderHeader(Ar));
                UniformScalarParameters = Ar.ReadArray(() => new FMaterialScalarParameterInfo(Ar));
                UniformVectorParameters = Ar.ReadArray(() => new FMaterialVectorParameterInfo(Ar));
                UniformTextureParameters = new FMaterialTextureParameterInfo[5][];
                Ar.ReadArray(UniformTextureParameters, () => Ar.ReadArray(() => new FMaterialTextureParameterInfo(Ar)));
                UniformExternalTextureParameters = Ar.ReadArray(() => new FMaterialExternalTextureParameterInfo(Ar));
                UniformPreshaderData = new FMaterialPreshaderData(Ar);
                VTStacks = Ar.ReadArray(() => new FMaterialVirtualTextureStack(Ar));
                ParameterCollections = Ar.ReadArray<FGuid>();
                UniformBufferLayoutInitializer = new FRHIUniformBufferLayoutInitializer(Ar);
            }
        }
    }

    public class FMaterialScalarParameterInfo
    {
        public readonly FMemoryImageMaterialParameterInfo? ParameterInfo;
        public readonly FHashedMaterialParameterInfo? ParameterInfoOld;
        public readonly string ParameterName;
        public readonly float DefaultValue;

        public FMaterialScalarParameterInfo(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE4_26)
            {
                ParameterInfo = new FMemoryImageMaterialParameterInfo(Ar);
            }
            else
            {
                ParameterInfoOld = new FHashedMaterialParameterInfo(Ar);
                ParameterName = Ar.ReadFString();
            }
            DefaultValue = Ar.Read<float>();
            if (Ar.Game < EGame.GAME_UE4_26)
            {
                Ar.Position +=4;
            }
        }
    }

    public class FMaterialVectorParameterInfo
    {
        public readonly FMemoryImageMaterialParameterInfo? ParameterInfo;
        public readonly FHashedMaterialParameterInfo? ParameterInfoOld;
        public readonly string ParameterName;
        public readonly FLinearColor DefaultValue;

        public FMaterialVectorParameterInfo(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE4_26)
            {
                ParameterInfo = new FMemoryImageMaterialParameterInfo(Ar);
            }
            else
            {
                ParameterInfoOld = new FHashedMaterialParameterInfo(Ar);
                ParameterName = Ar.ReadFString();
            }
            DefaultValue = Ar.Read<FLinearColor>();
        }
    }

    public class FMaterialUniformPreshaderHeader
    {
        public readonly uint OpcodeOffset;
        public readonly uint OpcodeSize;
        public readonly uint? BufferOffset;
        public readonly EValueComponentType? ComponentType;
        public readonly byte? NumComponents;
        public readonly uint? FieldIndex;
        public readonly uint? NumFields;

        public FMaterialUniformPreshaderHeader(FMemoryImageArchive Ar)
        {
            OpcodeOffset = Ar.Read<uint>();
            OpcodeSize = Ar.Read<uint>();

            if (Ar.Game == EGame.GAME_UE5_0)
            {
                BufferOffset = Ar.Read<uint>();
                ComponentType = Ar.Read<EValueComponentType>();
                NumComponents = Ar.Read<byte>();
                Ar.Position += 2;
            }
            else if (Ar.Game >= EGame.GAME_UE5_1)
            {
                FieldIndex = Ar.Read<uint>();
                NumFields = Ar.Read<uint>();
            }
        }
    }


    [StructLayout(LayoutKind.Sequential, Size = 12)]
    public struct FMaterialUniformPreshaderField
    {
        public uint BufferOffset, ComponentIndex;
        public EShaderValueType Type;
    }

    public enum EShaderValueType : byte
    {
        Void,

        Float1,
        Float2,
        Float3,
        Float4,

        Double1,
        Double2,
        Double3,
        Double4,

        Int1,
        Int2,
        Int3,
        Int4,

        Bool1,
        Bool2,
        Bool3,
        Bool4,

        // Any scalar/vector type
        Numeric1,
        Numeric2,
        Numeric3,
        Numeric4,

        // float4x4
        Float4x4,

        // Both of these are double4x4 on CPU
        // On GPU, they map to FLWCMatrix and FLWCInverseMatrix
        Double4x4,
        DoubleInverse4x4,

        // Any matrix type
        Numeric4x4,

        Struct,
        Object,
        Any,

        Num,
    }

    public class FMaterialNumericParameterInfo
    {
        public FMemoryImageMaterialParameterInfo ParameterInfo;
        public EMaterialParameterType ParameterType;
        public uint DefaultValueOffset;

        public FMaterialNumericParameterInfo(FMemoryImageArchive Ar)
        {
            ParameterInfo = new FMemoryImageMaterialParameterInfo(Ar);
            ParameterType = Ar.Read<EMaterialParameterType>();
            Ar.Position += 3;
            DefaultValueOffset = Ar.Read<uint>();
        }
    }

    public enum EMaterialParameterType : byte
    {
        Scalar = 0,
        Vector,
        DoubleVector,
        Texture,
        Font,
        RuntimeVirtualTexture,

        NumRuntime, // Runtime parameter types must go above here, and editor-only ones below

        StaticSwitch = NumRuntime,
        StaticComponentMask,

        Num,
        None = 0xff,
    }

    public class FMaterialTextureParameterInfo
    {
        public FMemoryImageMaterialParameterInfo? ParameterInfo;
        public FHashedMaterialParameterInfo? ParameterInfoOld;
        public readonly string ParameterName;
        public int TextureIndex = -1;
        public ESamplerSourceMode SamplerSource;
        public byte VirtualTextureLayerIndex = 0;

        public FMaterialTextureParameterInfo(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE4_26)
            {
                ParameterInfo = new FMemoryImageMaterialParameterInfo(Ar);
            }
            else
            {
                ParameterInfoOld = new FHashedMaterialParameterInfo(Ar);
                ParameterName = Ar.ReadFString();
            }
            TextureIndex = Ar.Read<int>();
            SamplerSource = Ar.Read<ESamplerSourceMode>();
            VirtualTextureLayerIndex = Ar.Read<byte>();
            Ar.Position += 2;
        }
    }

    public class FMemoryImageMaterialParameterInfo
    {
        public FName Name;
        public int Index;
        public EMaterialParameterAssociation Association;

        public FMemoryImageMaterialParameterInfo(FMemoryImageArchive Ar)
        {
            Name = Ar.ReadFName();
            Index = Ar.Read<int>();
            Association = Ar.Read<EMaterialParameterAssociation>();
            Ar.Position += 3;
        }
    }

    public class FHashedMaterialParameterInfo
    {
        public FHashedName Name;
        public int Index;
        public EMaterialParameterAssociation Association;

        public FHashedMaterialParameterInfo(FMemoryImageArchive Ar)
        {
            Name = Ar.Read<FHashedName>();
            Index = Ar.Read<int>();
            Association = Ar.Read<EMaterialParameterAssociation>();
            Ar.Position += 3;
        }
    }

    public enum ESamplerSourceMode : byte
    {
        SSM_FromTextureAsset,
        SSM_Wrap_WorldGroupSettings,
        SSM_Clamp_WorldGroupSettings
    }

    public class FMaterialExternalTextureParameterInfo
    {
        public FName ParameterName;
        public FGuid ExternalTextureGuid;
        public int SourceTextureIndex;

        public FMaterialExternalTextureParameterInfo(FMemoryImageArchive Ar)
        {
            ParameterName = Ar.ReadFName();
            ExternalTextureGuid = Ar.Read<FGuid>();
            SourceTextureIndex = Ar.Read<int>();
        }
    }

    public class FMaterialPreshaderData
    {
        public FName[]? Names;
        public uint[]? NamesOffset;
        public FPreshaderStructType[]? StructTypes;
        public EValueComponentType[]? StructComponentTypes;
        public byte[] Data;

        public FMaterialPreshaderData(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE4_26)
            {
                Names = Ar.ReadArray(Ar.ReadFName);
            }

            if (Ar.Game == EGame.GAME_UE5_0)
            {
                NamesOffset = Ar.ReadArray<uint>();
            }
            else if (Ar.Game >= EGame.GAME_UE5_1)
            {
                StructTypes = Ar.ReadArray<FPreshaderStructType>();
                StructComponentTypes = Ar.ReadArray<EValueComponentType>();
            }

            Data = Ar.ReadArray<byte>();
        }
    }

    public struct FPreshaderStructType
    {
        public ulong Hash;
        public int ComponentTypeIndex;
        public int NumComponents;
    }

    public enum EValueComponentType : byte
    {
        Void,
        Float,
        Double,
        Int,
        Bool,

        // May be any numeric type, stored internally as 'double' within FValue
        Numeric,

        Num,
    }

    public class FMaterialVirtualTextureStackOld
    {
        public uint NumLayers;
        public readonly int[] LayerUniformExpressionIndices = new int[8];
        public int PreallocatedStackTextureIndex;

        public FMaterialVirtualTextureStackOld(FArchive Ar)
        {
            NumLayers = Ar.Read<uint>();
            Ar.ReadArray(LayerUniformExpressionIndices);
            PreallocatedStackTextureIndex = Ar.Read<int>();
        }
    }

    public class FMaterialVirtualTextureStack
    {
        public uint NumLayers;
        public readonly int[] LayerUniformExpressionIndices = new int[8];
        public int PreallocatedStackTextureIndex;

        public FMaterialVirtualTextureStack(FMemoryImageArchive Ar)
        {
            NumLayers = Ar.Read<uint>();
            Ar.ReadArray(LayerUniformExpressionIndices);
            PreallocatedStackTextureIndex = Ar.Read<int>();
        }
    }

    public class FRHIUniformBufferLayoutInitializer
    {
        public string Name;
        public FRHIUniformBufferResource[] Resources;
        public FRHIUniformBufferResource[] GraphResources;
        public FRHIUniformBufferResource[] GraphTextures;
        public FRHIUniformBufferResource[] GraphBuffers;
        public FRHIUniformBufferResource[] GraphUniformBuffers;
        public FRHIUniformBufferResource[] UniformBuffers;
        public uint Hash = 0;
        public uint ConstantBufferSize = 0;
        public ushort RenderTargetsOffset = ushort.MaxValue;
        public byte /*FUniformBufferStaticSlot*/
            StaticSlot = 255;
        public EUniformBufferBindingFlags BindingFlags = EUniformBufferBindingFlags.Shader;
        public bool bHasNonGraphOutputs = false;
        public bool bNoEmulatedUniformBuffer = false;

        public FRHIUniformBufferLayoutInitializer(FMemoryImageArchive Ar)
        {
            if (Ar.Game >= EGame.GAME_UE5_0)
            {
                Name = Ar.ReadFString();
                Resources = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphResources = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphTextures = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphUniformBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                UniformBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                Hash = Ar.Read<uint>();
                ConstantBufferSize = Ar.Read<uint>();
                RenderTargetsOffset = Ar.Read<ushort>();
                StaticSlot = Ar.Read<byte>();
                BindingFlags = Ar.Read<EUniformBufferBindingFlags>();
                bHasNonGraphOutputs = Ar.ReadFlag();
                bNoEmulatedUniformBuffer = Ar.ReadFlag();
                Ar.Position += 2;
            }
            else if (Ar.Game >= EGame.GAME_UE4_26)
            {
                ConstantBufferSize = Ar.Read<uint>();
                StaticSlot = Ar.Read<byte>();
                Ar.Position +=1;
                RenderTargetsOffset = Ar.Read<ushort>();
                bHasNonGraphOutputs = Ar.ReadFlag();
                Ar.Position +=7;
                Resources = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphResources = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphTextures = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                GraphUniformBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                UniformBuffers = Ar.ReadArray<FRHIUniformBufferResource>();
                uint NumUsesForDebugging = Ar.Read<uint>();
                Ar.Position += 4;
                Name = Ar.ReadFString();
                Hash = Ar.Read<uint>();
                Ar.Position += 4;
            }
            else//4.25
            {
                ConstantBufferSize = Ar.Read<uint>();
                StaticSlot = Ar.Read<byte>();
                Ar.Position += 3;
                Resources = Ar.ReadArray<FRHIUniformBufferResource>();
                uint NumUsesForDebugging = Ar.Read<uint>();
                Ar.Position += 4;
                Name = Ar.ReadFString();
                Hash = Ar.Read<uint>();
                Ar.Position += 4;
            }
        }
    }

    public struct FRHIUniformBufferLayout
    {
        public uint ConstantBufferSize;
        public ushort[] ResourceOffsets;
        public byte[] Resources;

        public FRHIUniformBufferLayout(FArchive Ar)
        {
            ConstantBufferSize = Ar.Read<uint>();
            ResourceOffsets = Ar.ReadArray<ushort>();
            Resources = Ar.ReadArray<byte>();
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 4)]
    public struct FRHIUniformBufferResource
    {
        public ushort MemberOffset;
        public EUniformBufferBaseType MemberType;
    }

    public enum EUniformBufferBindingFlags : byte
    {
        Shader = 1 << 0,
        Static = 1 << 1,
        StaticAndShader = Static | Shader
    }

    public class FShaderMapResourceCode
    {
        public FSHAHash ResourceHash;
        public FSHAHash[] ShaderHashes;
        public FShaderEntry[] ShaderEntries;

        public FShaderMapResourceCode(FArchive Ar)
        {
            ResourceHash = new FSHAHash(Ar);
            ShaderHashes = Ar.ReadArray(() => new FSHAHash(Ar));
            ShaderEntries = Ar.ReadArray(() => new FShaderEntry(Ar));
        }
    }

    public class FShaderEntry
    {
        public byte[] Code; // Don't Serialize
        public int UncompressedSize;
        public byte Frequency; // Enum

        public FShaderEntry(FArchive Ar)
        {
            Code = Ar.ReadArray<byte>();
            UncompressedSize = Ar.Read<int>();
            Frequency = Ar.Read<byte>();
        }
    }

    public class FMemoryImageResult
    {
        public FPlatformTypeLayoutParameters LayoutParameters;
        public byte[] FrozenObject;
        public FPointerTableBase PointerTable;
        public FMemoryImageVTable[] VTables;
        public FMemoryImageName[] ScriptNames;
        public FMemoryImageName[] MinimalNames;

        public FMemoryImageResult(FPointerTableBase pointerTable)
        {
            PointerTable = pointerTable;
        }

        public void LoadFromArchive(FArchive Ar)
        {
            var bUseNewFormat = Ar.Versions["ShaderMap.UseNewCookedFormat"];

            LayoutParameters = bUseNewFormat ? new FPlatformTypeLayoutParameters(Ar) : new();

            var frozenSize = Ar.Read<uint>();
            FrozenObject = Ar.ReadBytes((int) frozenSize);

            if (bUseNewFormat)
            {
                PointerTable.LoadFromArchive(Ar, true);
            }

            var numVTables = Ar.Read<int>();
            var numScriptNames = Ar.Read<int>();
            var numMinimalNames = Ar.Game >= EGame.GAME_UE4_26 ? Ar.Read<int>() : 0;
            VTables = Ar.ReadArray(numVTables, () => new FMemoryImageVTable(Ar));
            ScriptNames = Ar.ReadArray(numScriptNames, () => new FMemoryImageName(Ar));
            MinimalNames = Ar.ReadArray(numMinimalNames, () => new FMemoryImageName(Ar));

            if (!bUseNewFormat)
            {
                PointerTable.LoadFromArchive(Ar, false);
            }
        }

        internal Dictionary<int, FName> GetNames()
        {
            var names = new Dictionary<int, FName>();

            foreach (var name in ScriptNames)
            {
                foreach (var patch in name.Patches)
                {
                    names[patch.Offset] = name.Name;
                }
            }
            foreach (var name in MinimalNames)
            {
                foreach (var patch in name.Patches)
                {
                    names[patch.Offset] = name.Name;
                }
            }

            return names;
        }

        public struct FMemoryImageVTablePatch
        {
            public int VTableOffset;
            public int Offset;
        }

        public class FMemoryImageVTable
        {
            public ulong TypeNameHash;
            public FMemoryImageVTablePatch[] Patches;

            public FMemoryImageVTable(FArchive Ar)
            {
                TypeNameHash = Ar.Read<ulong>();
                Patches = Ar.ReadArray<FMemoryImageVTablePatch>();
            }
        }

        public struct FMemoryImageNamePatch
        {
            public int Offset;
        }

        public class FMemoryImageName
        {
            public FName Name;
            public FMemoryImageNamePatch[] Patches;

            public FMemoryImageName(FArchive Ar)
            {
                Name = Ar.ReadFName();
                Patches = Ar.ReadArray<FMemoryImageNamePatch>();
            }

            public override string ToString() => $"{Name}: x{Patches.Length} Patches";
        }
    }

    public class FShaderMapPointerTable : FPointerTableBase
    {
        //public int NumTypes, NumVFTypes;
        public FHashedName[]? Types;
        public FHashedName[]? VFTypes;

        public override void LoadFromArchive(FArchive Ar, bool bUseNewFormat)
        {
            if (bUseNewFormat) base.LoadFromArchive(Ar, bUseNewFormat);
            var NumTypes = Ar.Read<int>();
            var NumVFTypes = Ar.Read<int>();
            Types = Ar.ReadArray<FHashedName>(NumTypes);
            VFTypes = Ar.ReadArray<FHashedName>(NumVFTypes);
            if (!bUseNewFormat) base.LoadFromArchive(Ar, bUseNewFormat);
        }
    }

    public struct FHashedName
    {
        public ulong Hash;

        public FHashedName(FArchive Ar)
        {
            Hash = Ar.Read<ulong>();
        }
    }

    public class FPointerTableBase
    {
        public FTypeLayoutDesc[] TypeDependencies;

        public virtual void LoadFromArchive(FArchive Ar, bool bUseNewFormat)
        {
            TypeDependencies = Ar.ReadArray(() => new FTypeLayoutDesc(Ar, bUseNewFormat));
        }
    }

    public class FTypeLayoutDesc
    {
        public readonly FName? Name;
        public readonly FHashedName? NameHash;
        public readonly uint SavedLayoutSize;
        public readonly FSHAHash SavedLayoutHash;

        public FTypeLayoutDesc(FArchive Ar, bool bUseNewFormat)
        {
            if (bUseNewFormat)
            {
                Name = Ar.ReadFName();
            }
            else
            {
                NameHash = Ar.Read<FHashedName>();
            }
            SavedLayoutSize = Ar.Read<uint>();
            SavedLayoutHash = new FSHAHash(Ar);
        }
    }


    public abstract class FMaterialUniformExpression
    {
        public static FMaterialUniformExpression MatchParameterType(FArchive Ar)
        {
            FName TypeName = Ar.ReadFName();
            if (TypeName.ToString() == "FMaterialUniformExpressionVectorParameter")
            {
                return new FMaterialUniformExpressionVectorParameter();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionAppendVector")
            {
                return new FMaterialUniformExpressionAppendVector();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionClamp")
            {
                return new FMaterialUniformExpressionClamp();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionComponentSwizzle")
            {
                return new FMaterialUniformExpressionComponentSwizzle();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionFoldedMath")
            {
                return new FMaterialUniformExpressionFoldedMath();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionScalarParameter")
            {
                return new FMaterialUniformExpressionScalarParameter();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionConstant")
            {
                return new FMaterialUniformExpressionConstant();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionTexture")
            {
                return new FMaterialUniformExpressionTexture();
            }
            if (TypeName.ToString() == "FMaterialUniformExpressionTextureParameter")
            {
                return new FMaterialUniformExpressionTextureParameter();
            }

            Log.Warning("Loading an unknown uniform expression type '{0}'!", Ar.Name);
            return null!;
        }
        public abstract void Deserialize(FArchive Ar);
    }

    public class FMaterialUniformExpressionScalarParameter : FMaterialUniformExpression
    {
        public FMaterialParameterInfo ParameterInfo;
        public float DefaultValue;

        public override void Deserialize(FArchive Ar)
        {
            ParameterInfo = new FMaterialParameterInfo(Ar);
            DefaultValue = Ar.Read<float>();
        }
    }

    public class FMaterialUniformExpressionVectorParameter : FMaterialUniformExpression
    {
        public FMaterialParameterInfo ParameterInfo;
        public FLinearColor DefaultValue;

        public override void Deserialize(FArchive Ar)
        {
            ParameterInfo = new FMaterialParameterInfo(Ar);
            DefaultValue = Ar.Read<FLinearColor>();
        }
    }

    public class FMaterialUniformExpressionAppendVector : FMaterialUniformExpression
    {
        FMaterialUniformExpression A;
        FMaterialUniformExpression B;
        uint NumComponentsA;

        public override void Deserialize(FArchive Ar)
        {
            A = MatchParameterType(Ar);
            A.Deserialize(Ar);
            B = MatchParameterType(Ar);
            B.Deserialize(Ar);
            NumComponentsA = Ar.Read<uint>();
        }
    }

    public class FMaterialUniformExpressionClamp : FMaterialUniformExpression
    {
        FMaterialUniformExpression Input;
        FMaterialUniformExpression Min;
        FMaterialUniformExpression Max;

        public override void Deserialize(FArchive Ar)
        {
            Input = MatchParameterType(Ar);
            Input.Deserialize(Ar);
            Min = MatchParameterType(Ar);
            Min.Deserialize(Ar);
            Max = MatchParameterType(Ar);
            Max.Deserialize(Ar);
        }
    }
    public class FMaterialUniformExpressionComponentSwizzle : FMaterialUniformExpression
    {
        public FMaterialUniformExpression X;
        public byte IndexR;
        public byte IndexG;
        public byte IndexB;
        public byte IndexA;
        public byte NumElements;

        public override void Deserialize(FArchive Ar)
        {
            X = MatchParameterType(Ar);
            X.Deserialize(Ar);
            IndexR = Ar.Read<byte>();
            IndexG = Ar.Read<byte>();
            IndexB = Ar.Read<byte>();
            IndexA = Ar.Read<byte>();
            NumElements = Ar.Read<byte>();
        }
    }
    public class FMaterialUniformExpressionFoldedMath : FMaterialUniformExpression
    {
        public FMaterialUniformExpression A;
        public FMaterialUniformExpression B;
        public uint ValueType;
        public byte Op;

        public override void Deserialize(FArchive Ar)
        {
            A = MatchParameterType(Ar);
            A.Deserialize(Ar);
            B = MatchParameterType(Ar);
            B.Deserialize(Ar);
            ValueType = Ar.Read<uint>();
            Op = Ar.Read<byte>();
        }
    }

    public class FMaterialUniformExpressionConstant : FMaterialUniformExpression
    {
        public FLinearColor Value;
        public byte ValueType;
        public override void Deserialize(FArchive Ar)
        {
            Value = Ar.Read<FLinearColor>();
            ValueType = Ar.Read<byte>();
        }
    }

    public class FMaterialUniformExpressionTexture : FMaterialUniformExpression
    {
        public int TextureIndex;
        public short TextureLayerIndex;
        public short PageTableLayerIndex;
        public ESamplerSourceMode SamplerSource;
        public bool bVirtualTexture;

        public override void Deserialize(FArchive Ar)
        {
            TextureIndex = Ar.Read<int>();
            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                TextureLayerIndex = Ar.Read<short>();
                PageTableLayerIndex = Ar.Read<short>();
            }
            SamplerSource = (ESamplerSourceMode)Ar.Read<int>();
            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                bVirtualTexture = Ar.Read<bool>();
            }
        }
    }

    public class FMaterialUniformExpressionTextureParameter : FMaterialUniformExpressionTexture
    {
        public FMaterialParameterInfo ParameterInfo;

        public override void Deserialize(FArchive Ar)
        {
            ParameterInfo = new FMaterialParameterInfo(Ar);
            base.Deserialize(Ar);
        }
    }

    public class FUniformExpressionSetOld
    {
        public FMaterialUniformExpression[] UniformVectorExpressions;
        public FMaterialUniformExpression[] UniformScalarExpressions;
        public FMaterialUniformExpressionTexture[] Uniform2DTextureExpressions;
        public FMaterialUniformExpressionTexture[] UniformCubeTextureExpressions;
        public FMaterialUniformExpressionTexture[] Uniform2DArrayTextureExpressions;
        public FMaterialUniformExpressionTexture[] UniformVolumeTextureExpressions;
        public FMaterialUniformExpressionTexture[] UniformVirtualTextureExpressions;
        public FMaterialUniformExpressionTexture[] UniformExternalTextureExpressions;

        public FMaterialVirtualTextureStackOld[] VTStacks;
        public FGuid[] ParameterCollections;

        public FUniformExpressionSetOld(FArchive Ar)
        {
            UniformVectorExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return Expression;
            });
            UniformScalarExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return Expression;
            });
            Uniform2DTextureExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return (FMaterialUniformExpressionTexture)Expression;
            });
            UniformCubeTextureExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return (FMaterialUniformExpressionTexture)Expression;
            });
            UniformVolumeTextureExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return (FMaterialUniformExpressionTexture)Expression;
            });
            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                UniformVirtualTextureExpressions = Ar.ReadArray(() =>
                {
                    var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                    Expression.Deserialize(Ar);
                    return (FMaterialUniformExpressionTexture)Expression;
                });
            }
            UniformExternalTextureExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return (FMaterialUniformExpressionTexture)Expression;
            });
            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                VTStacks = Ar.ReadArray(() => new FMaterialVirtualTextureStackOld(Ar));
            }
            Uniform2DArrayTextureExpressions = Ar.ReadArray(() =>
            {
                var Expression = FMaterialUniformExpression.MatchParameterType(Ar);
                Expression.Deserialize(Ar);
                return (FMaterialUniformExpressionTexture)Expression;
            });
            ParameterCollections = Ar.ReadArray<FGuid>();
        }
    }

    public class FMaterialCompilationOutputOld
    {
        public FUniformExpressionSetOld UniformExpressionSet;
        public uint UsedSceneTextures;
        public byte NumUsedUVScalars;
        public byte NumUsedCustomInterpolatorScalars;
        public short EstimatedNumTextureSamplesVS;
        public short EstimatedNumTextureSamplesPS;

        public bool bRequiresSceneColorCopy;
        public bool bNeedsSceneTextures;
        public bool bUsesEyeAdaptation;
        public bool bModifiesMeshPosition;
        public bool bUsesWorldPositionOffset;
        public bool bNeedsGBuffer;
        public bool bUsesGlobalDistanceField;
        public bool bUsesPixelDepthOffset;
        public bool bUsesSceneDepthLookup;
        public bool bUsesDistanceCullFade;
        public bool bHasRuntimeVirtualTextureOutput;

        public FMaterialCompilationOutputOld(FArchive Ar)
        {
            UniformExpressionSet = new FUniformExpressionSetOld(Ar);
            if (Ar.Game >= EGame.GAME_UE4_23)
            {
                UsedSceneTextures = Ar.Read<uint>();
                Ar.Read<ulong>();

                byte PackedFlags = Ar.Read<byte>();

                bRequiresSceneColorCopy = ((PackedFlags >> 0) & 1) != 0;
                bModifiesMeshPosition = ((PackedFlags >> 1) & 1) != 0;
                bUsesWorldPositionOffset = ((PackedFlags >> 0) & 2) != 0;
                bUsesGlobalDistanceField = ((PackedFlags >> 0) & 3) != 0;
                bUsesPixelDepthOffset = ((PackedFlags >> 0) & 4) != 0;
                bUsesDistanceCullFade = ((PackedFlags >> 0) & 5) != 0;
                bHasRuntimeVirtualTextureOutput = ((PackedFlags >> 0) & 6) != 0;
            }
            else
            {
                NumUsedUVScalars = Ar.Read<byte>();
                NumUsedCustomInterpolatorScalars = Ar.Read<byte>();
                EstimatedNumTextureSamplesVS = Ar.Read<short>();
                EstimatedNumTextureSamplesPS = Ar.Read<short>();
                bRequiresSceneColorCopy = Ar.Read<bool>();
                Ar.Position += 3;
                bNeedsSceneTextures = Ar.Read<bool>();
                Ar.Position += 3;
                bUsesEyeAdaptation = Ar.Read<bool>();
                Ar.Position += 3;
                bModifiesMeshPosition = Ar.Read<bool>();
                Ar.Position += 3;
                bUsesWorldPositionOffset = Ar.Read<bool>();
                Ar.Position += 3;
                bNeedsGBuffer = Ar.Read<bool>();
                Ar.Position += 3;
                bUsesGlobalDistanceField = Ar.Read<bool>();
                Ar.Position += 3;
                bUsesPixelDepthOffset = Ar.Read<bool>();
                Ar.Position += 3;
                bUsesSceneDepthLookup = Ar.Read<bool>();
                Ar.Position += 3;
            }
        }
    }


    public abstract class TShaderMap
    {
        public struct FSerializedShaderPipeline
        {
            public FName ShaderPipelineType;
            public FShaderOld[] ShaderStages;
        }

        public FName[] ShaderTypes;
        public FShaderOld[] Shaders;
        public FName[] ShaderPipelineTypes;
        public FSerializedShaderPipeline[] ShaderPipelines;

        public void Deserialize(FMaterialResourceProxyReader Ar)
        {
            int NumShaders = Ar.Read<int>();
            ShaderTypes = new FName[NumShaders];
            Shaders = new FShaderOld[NumShaders];
            for (int ShaderIndex = 0; ShaderIndex < NumShaders; ShaderIndex++)
            {
                ShaderTypes[ShaderIndex] = Ar.ReadFName();
                long EndOffset = Ar.Read<long>();
                var Shader = FShaderOld.MatchParameterType(ShaderTypes[ShaderIndex]);
                if (Shader != null)
                {
                    Shader.DeserializeBase(Ar);
                    Shaders[ShaderIndex] = Shader;
                }
                else
                {
                    Ar.Position += EndOffset;
                }
            }
            int NumPipelines = Ar.Read<int>();
            ShaderPipelineTypes = new FName[NumPipelines];
            ShaderPipelines = new FSerializedShaderPipeline[NumPipelines];
            for (int PipelineIndex = 0; PipelineIndex < NumPipelines; PipelineIndex++)
            {
                ShaderPipelineTypes[PipelineIndex] = Ar.ReadFName();
                FSerializedShaderPipeline SerializedPipeline = new();
                int NumStages = Ar.Read<int>();
                SerializedPipeline.ShaderStages = new FShaderOld[NumStages];
                foreach (int index in Enumerable.Range(0, NumStages))
                {
                    SerializedPipeline.ShaderPipelineType = Ar.ReadFName();
                    long EndOffset = Ar.Read<long>();
                    var Shader = FShaderOld.MatchParameterType(SerializedPipeline.ShaderPipelineType);
                    if (Shader != null)
                    {
                        Shader.DeserializeBase(Ar);
                        SerializedPipeline.ShaderStages[index] = Shader;
                    }
                    else
                    {
                        Ar.Position += EndOffset;
                    }
                }
                ShaderPipelines[PipelineIndex] = SerializedPipeline;
            }
        }
    }

    public class FMaterialShaderMapOld : TShaderMap
    {
        public FMaterialShaderMapId ShaderMapId;
        public EShaderPlatform Platform;
        public string FriendlyName;
        public FMaterialCompilationOutputOld MaterialCompilationOutput;
        public string DebugDescription;
        public FMeshMaterialShaderMapOld[] MeshShaderMaps;
        public bool bCooked;
        public new void Deserialize(FMaterialResourceProxyReader Ar)
        {
            ShaderMapId = new FMaterialShaderMapId(Ar);
            Platform = (EShaderPlatform)Ar.Read<int>();
            FriendlyName = Ar.ReadFString();
            MaterialCompilationOutput = new FMaterialCompilationOutputOld(Ar);
            DebugDescription = Ar.ReadFString();

            base.Deserialize(Ar);
            int NumMeshShaderMaps = Ar.Read<int>();
            MeshShaderMaps = new FMeshMaterialShaderMapOld[NumMeshShaderMaps];
            foreach (int VFIndex in Enumerable.Range(0, NumMeshShaderMaps))
            {
                ulong VFType = Ar.Read<ulong>();
                FMeshMaterialShaderMapOld MeshShaderMap = new FMeshMaterialShaderMapOld();
                MeshShaderMap.Deserialize(Ar);
                MeshShaderMaps[VFIndex] = MeshShaderMap;
            }
            bCooked = Ar.Read<bool>();
        }
    }

    public class FMaterialShaderMap : FShaderMapBase
    {
        public FMaterialShaderMapId ShaderMapId;

        public new void Deserialize(FMaterialResourceProxyReader Ar)
        {
            ShaderMapId = new FMaterialShaderMapId(Ar);
            base.Deserialize(Ar);
        }

        protected override FShaderMapContent ReadContent(FMemoryImageArchive Ar) => new FMaterialShaderMapContent(Ar);
    }

    public class FShaderTypeDependency
    {
        public FName ShaderTypeName;
        public FSHAHash SourceHash;
        public int PermutationId;

        public FShaderTypeDependency(FArchive Ar)
        {
            ShaderTypeName = Ar.ReadFName();
            SourceHash = new FSHAHash(Ar);
            PermutationId = Ar.Read<int>();
        }
    }

    public class FShaderPipelineTypeDependency
    {
        public FName ShaderPipelineType;
        public FSHAHash SourceHash;

        public FShaderPipelineTypeDependency(FArchive Ar)
        {
            ShaderPipelineType = Ar.ReadFName();
            SourceHash = new FSHAHash(Ar);
        }
    }

    public class FVertexFactoryTypeDependency
    {
        public FName VertexFactoryType;
        public FSHAHash SourceHash;

        public FVertexFactoryTypeDependency(FArchive Ar)
        {
            VertexFactoryType = Ar.ReadFName();
            SourceHash = new FSHAHash(Ar);
        }
    }

    public class FMaterialShaderMapId
    {
        public EMaterialShaderMapUsage Usage;
        public FGuid BaseMaterialId;
        public EMaterialQualityLevel QualityLevel;
        public ERHIFeatureLevel FeatureLevel;
        public FStaticParameterSet ParameterSet;
        public FGuid[] ReferencedFunctions;
        public FGuid[] ReferencedParameterCollections;
        public FShaderTypeDependency[] ShaderTypeDependencies;
        public FShaderPipelineTypeDependency[] ShaderPipelineTypeDependencies;
        public FVertexFactoryTypeDependency[] VertexFactoryTypeDependencies;
        public FSHAHash TextureReferencesHash;
        public FSHAHash BasePropertyOverridesHash;
        public FSHAHash CookedShaderMapIdHash;
        public FPlatformTypeLayoutParameters? LayoutParams;

        public FMaterialShaderMapId(FArchive Ar)
        {
            if (Ar.Game < EGame.GAME_UE4_23)
            {
                Usage = (EMaterialShaderMapUsage) Ar.Read<uint>();
                BaseMaterialId = Ar.Read<FGuid>();
            }

            var bIsLegacyPackage = Ar.Ver < EUnrealEngineObjectUE4Version.PURGED_FMATERIAL_COMPILE_OUTPUTS;

            if (!bIsLegacyPackage)
            {
                QualityLevel = Ar.Game >= EGame.GAME_UE5_2 ? (EMaterialQualityLevel) Ar.Read<byte>() : (EMaterialQualityLevel) Ar.Read<int>();//changed to byte in FN 23.20
                FeatureLevel = (ERHIFeatureLevel) Ar.Read<int>();
            }
            else
            {
                var legacyQualityLevel = (EMaterialQualityLevel) Ar.Read<byte>(); // Is it enum?
            }

            if (Ar.Game < EGame.GAME_UE4_23)
            {
                ParameterSet = new FStaticParameterSet(Ar);
                ReferencedFunctions = Ar.ReadArray<FGuid>();
                if (Ar.Ver >= EUnrealEngineObjectUE4Version.COLLECTIONS_IN_SHADERMAPID)
                {
                    ReferencedParameterCollections = Ar.ReadArray<FGuid>();
                }
                if (Ar.CustomVer(FEditorObjectVersion.GUID) >=
                    (int) FEditorObjectVersion.Type.AddedMaterialSharedInputs
                    && Ar.CustomVer(FReleaseObjectVersion.GUID) <
                    (int)FReleaseObjectVersion.Type.RemovedMaterialSharedInputCollection)
                {
                    Ar.ReadArray<FGuid>();
                }

                ShaderTypeDependencies = Ar.ReadArray(() => new FShaderTypeDependency(Ar));
                if (Ar.Ver >= EUnrealEngineObjectUE4Version.PURGED_FMATERIAL_COMPILE_OUTPUTS)
                {
                    ShaderPipelineTypeDependencies = Ar.ReadArray(() => new FShaderPipelineTypeDependency(Ar));
                }
                VertexFactoryTypeDependencies = Ar.ReadArray(() => new FVertexFactoryTypeDependency(Ar));
                if (Ar.Ver >= EUnrealEngineObjectUE4Version.PURGED_FMATERIAL_COMPILE_OUTPUTS)
                {
                    TextureReferencesHash = new FSHAHash(Ar);
                }
                else
                {
                    var temp = new FSHAHash(Ar);
                }
                if (Ar.Ver >= EUnrealEngineObjectUE4Version.MATERIAL_INSTANCE_BASE_PROPERTY_OVERRIDES)
                {
                    BasePropertyOverridesHash = new FSHAHash(Ar);
                }
            }
            else
            {
                CookedShaderMapIdHash = new FSHAHash(Ar);

                if (!bIsLegacyPackage && Ar.Game >= EGame.GAME_UE4_25)
                {
                    LayoutParams = new FPlatformTypeLayoutParameters(Ar);
                }
            }

        }
    }

    public class FPlatformTypeLayoutParameters
    {
        public uint MaxFieldAlignment;
        public EFlags Flags;

        public FPlatformTypeLayoutParameters()
        {
            MaxFieldAlignment = 0xffffffff;
        }

        public FPlatformTypeLayoutParameters(FArchive Ar)
        {
            MaxFieldAlignment = Ar.Read<uint>();
            Flags = Ar.Read<EFlags>();
        }

        [Flags]
        public enum EFlags
        {
            Flag_Initialized = 1 << 0,
            Flag_Is32Bit = 1 << 1,
            Flag_AlignBases = 1 << 2,
            Flag_WithEditorOnly = 1 << 3,
            Flag_WithRaytracing = 1 << 4,
        }
    }

    public enum EShaderPlatform : byte
    {
        SP_PCD3D_SM5					= 0,
        SP_METAL						= 11,
        SP_METAL_MRT					= 12,
        SP_PCD3D_ES3_1					= 14,
        SP_OPENGL_PCES3_1				= 15,
        SP_METAL_SM5					= 16,
        SP_VULKAN_PCES3_1				= 17,
        SP_METAL_SM5_NOTESS_REMOVED		= 18,
        SP_VULKAN_SM5					= 20,
        SP_VULKAN_ES3_1_ANDROID			= 21,
        SP_METAL_MACES3_1 				= 22,
        SP_OPENGL_ES3_1_ANDROID			= 24,
        SP_METAL_MRT_MAC				= 27,
        SP_VULKAN_SM5_LUMIN_REMOVED		= 28,
        SP_VULKAN_ES3_1_LUMIN_REMOVED	= 29,
        SP_METAL_TVOS					= 30,
        SP_METAL_MRT_TVOS				= 31,
        /**********************************************************************************/
        /* !! Do not add any new platforms here. Add them below SP_StaticPlatform_Last !! */
        /**********************************************************************************/

        //---------------------------------------------------------------------------------
        /** Pre-allocated block of shader platform enum values for platform extensions */
        SP_StaticPlatform_First = 32,

        DDPI_EXTRA_SHADERPLATFORMS,

        SP_StaticPlatform_Last  = SP_StaticPlatform_First + 16 - 1,

        //  Add new platforms below this line, starting from (SP_StaticPlatform_Last + 1)
        //---------------------------------------------------------------------------------
        SP_VULKAN_SM5_ANDROID			= SP_StaticPlatform_Last+1,
        SP_PCD3D_SM6,

        SP_NumPlatforms,
        SP_NumBits						= 7,
    }
}
