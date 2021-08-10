using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct SortingData
{
    public uint drawCallInstanceIndex; // 1
    public float distanceToCam;         // 2
};

[RequireComponent(typeof(Camera))]
public class CameraCulling : MonoBehaviour
{
    private new Camera camera;
    public ComputeShader frustumCullingComputeShader;
    public ComputeShader occlusionCullingComputeShader;
    public Renderer[] staticRenderers;
    private int frustumKernel;
    private int occlusionKernel;
    private uint threadSizeX;
    public Bounds[] staticRendererBounds;
    public ComputeBuffer boundBuffer;
    public HZB hiZBuffer; 

    // 为什么要保证resultBuffer不被序列化呢？

    [NonSerialized]
    public int[] cullingResults;
    public ComputeBuffer resultBuffer;

    [NonSerialized]
    public Plane[] frustumPlanes;
    public ComputeBuffer planeBuffer;

    [NonSerialized]
    public ComputeBuffer m_instancesSortingData;

    private Vector3 prevPos;
    private Quaternion prevRot;

    void Awake()
    {
        camera = GetComponent<Camera>();
        staticRenderers = Array.FindAll(FindObjectsOfType<Renderer>(), (renderer) => renderer.gameObject.isStatic);
        // Array.ConvertAll(TInput, TOutput) 
        // 将一种类型的数组转换为另一种类型的数组
        // 获取目标渲染物体的轴对齐包围盒
        staticRendererBounds = Array.ConvertAll(staticRenderers, (renderer) => renderer.bounds);

        Init();

        prevPos = camera.transform.position;
        prevRot = camera.transform.rotation;

        Cull();

        // 一个小优化
        // OnPreCull();
    }

    void OnDestroy() 
    {
        resultBuffer.Dispose();
        planeBuffer.Dispose();
        boundBuffer.Dispose();
    }

    private void Init()
    {
        // 使用FindKernel函数，用名字找到ComputeShader中定义的一个运算unit
        frustumKernel = frustumCullingComputeShader.FindKernel("OverlapFrustumAndBounds");

        if (frustumKernel < 0)
        {
            Debug.LogError("CameraCulling.Init >> frustum and bound kernel not exist..");
        }

        uint threadSizeY;
        uint threadSizeZ;

        // 获取GPU线程的X/Y/Z
        frustumCullingComputeShader.GetKernelThreadGroupSizes(frustumKernel, out threadSizeX, out threadSizeY, out threadSizeZ);

        if (staticRendererBounds == null)
            staticRendererBounds = Array.ConvertAll(staticRenderers, (renderer) => renderer.bounds);

        // 构造视锥平面
        frustumPlanes = new Plane[6];
        // 用Marshal.SizeOf()获取一个对象所占内存的大小
        planeBuffer = new ComputeBuffer(6, Marshal.SizeOf(typeof(Plane)));
        // 用数组内容初始化ComputeBuffer
        planeBuffer.SetData(frustumPlanes);

        boundBuffer = new ComputeBuffer(staticRenderers.Length, Marshal.SizeOf(typeof(Bounds)));
        boundBuffer.SetData(staticRendererBounds);

        if (cullingResults == null)
            cullingResults = new int[staticRenderers.Length];

        resultBuffer = new ComputeBuffer(staticRenderers.Length, Marshal.SizeOf(typeof(uint)));
        resultBuffer.SetData(cullingResults);

        // 把脚本的输入和ComputeShader的输入关联在一起
        // 输入的长度是所有渲染体的数量
        frustumCullingComputeShader.SetInt("resultLength", staticRenderers.Length);

        // 将ComputeBuffer关联到外部，给其它阶段的shader提供数据
        frustumCullingComputeShader.SetBuffer(frustumKernel, "bounds", boundBuffer);
        frustumCullingComputeShader.SetBuffer(frustumKernel, "planes", planeBuffer);
        frustumCullingComputeShader.SetBuffer(frustumKernel, "results", resultBuffer);

        // Occlusion Culling


        occlusionKernel = occlusionCullingComputeShader.FindKernel("CSMain");

        occlusionCullingComputeShader.SetBuffer(occlusionKernel, "bounds", boundBuffer);
        occlusionCullingComputeShader.SetBuffer(occlusionKernel, "results", resultBuffer);

        // List<SortingData> sortingData = new List<SortingData>();
        // for (int i = 0; i < staticRenderers.Length; i++)
        // {
        //     sortingData.Add(new SortingData() {
        //             drawCallInstanceIndex = (uint)0,
        //             distanceToCam = Vector3.Distance(staticRendererBounds[i].center, m_camPosition)
        //         });
        // }

        // m_instancesSortingData = new ComputeBuffer(staticRenderers.Length, Marshal.SizeOf(typeof(SortingData)));
        // m_instancesSortingData.SetData(sortingData);
        
        // occlusionCullingComputeShader.SetBuffer(occlusionKernel, "_SortingData", m_instancesSortingData);
        resultBuffer.SetData(cullingResults);
        

    }

    public void Cull()
    {
        // 计算视锥平面
        GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
        planeBuffer.SetData(frustumPlanes);

        uint length = (uint)staticRenderers.Length;
        uint dispatchXLength = length / threadSizeX + (uint)(((int)length % (int)threadSizeX > 0) ? 1 : 0);

        // 启动ComputeShader的运算unit
        // frustumCullingComputeShader.Dispatch(frustumKernel, (int)dispatchXLength, 1, 1);
        // resultBuffer.GetData(cullingResults);
        Matrix4x4 v = camera.worldToCameraMatrix;
        Matrix4x4 p = camera.projectionMatrix;
        Matrix4x4 m_MVP = p * v;
        Vector3 m_camPosition = camera.transform.position;

        occlusionCullingComputeShader.SetFloat("_ShadowDistance", QualitySettings.shadowDistance);
        occlusionCullingComputeShader.SetMatrix("_UNITY_MATRIX_MVP", m_MVP);
        occlusionCullingComputeShader.SetVector("_CamPosition", m_camPosition);

        occlusionCullingComputeShader.SetVector("_HiZTextureSize", hiZBuffer.TextureSize);
        occlusionCullingComputeShader.SetTexture(occlusionKernel, "_HiZMap", hiZBuffer.Texture);
        

        occlusionCullingComputeShader.Dispatch(occlusionKernel, (int)dispatchXLength, 1, 1);
        resultBuffer.GetData(cullingResults);

        // 根据resultBuffer的内容，确定是否渲染
        for (int i = 0; i < staticRenderers.Length; i++)
            staticRenderers[i].enabled = cullingResults[i] != 0;
    }


    private void OnPreCull()
    {
        if (prevPos != camera.transform.position || prevRot != camera.transform.rotation)
        {
            prevPos = camera.transform.position;
            prevRot = camera.transform.rotation;

            Cull();
        }
    }


    void Update()
    {

    }
}
