using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Camera))]
public class CameraCulling : MonoBehaviour
{
    private new Camera camera;
    public ComputeShader cullingComputeShader;
    public Renderer[] staticRenderers;
    private int kernel;
    private uint threadSizeX;
    public Bounds[] staticRendererBounds;
    public ComputeBuffer boundBuffer;

    // 为什么要保证resultBuffer不被序列化呢？

    [NonSerialized]
    public int[] cullingResults;
    public ComputeBuffer resultBuffer;

    [NonSerialized]
    public Plane[] frustumPlanes;
    public ComputeBuffer planeBuffer;

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
        OnPreCull();
    }


    private void Init()
    {
        // 使用FindKernel函数，用名字找到ComputeShader中定义的一个运算unit
        kernel = cullingComputeShader.FindKernel("OverlapFrustumAndBounds");

        if (kernel < 0)
        {
            Debug.LogError("CameraCulling.Init >> frustum and bound kernel not exist..");
        }

        uint threadSizeY;
        uint threadSizeZ;

        // 获取GPU线程的X/Y/Z
        cullingComputeShader.GetKernelThreadGroupSizes(kernel, out threadSizeX, out threadSizeY, out threadSizeZ);

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
        cullingComputeShader.SetInt("resultLength", staticRenderers.Length);

        // 将ComputeBuffer关联到外部，给其它阶段的shader提供数据
        cullingComputeShader.SetBuffer(kernel, "bounds", boundBuffer);
        cullingComputeShader.SetBuffer(kernel, "planes", planeBuffer);
        cullingComputeShader.SetBuffer(kernel, "results", resultBuffer);

    }

    public void Cull()
    {
        // 计算视锥平面
        GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
        planeBuffer.SetData(frustumPlanes);

        uint length = (uint)staticRenderers.Length;
        uint dispatchXLength = length / threadSizeX + (uint)(((int)length % (int)threadSizeX > 0) ? 1 : 0);

        // 启动ComputeShader的运算unit
        cullingComputeShader.Dispatch(kernel, (int)dispatchXLength, 1, 1);
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
