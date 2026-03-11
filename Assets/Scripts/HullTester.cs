using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using System.Linq;
using System;
using System.Diagnostics;
using Unity.Collections;
using Common;
using UnityNativeHull;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]  // 在编辑模式下执行
public class HullTester : MonoBehaviour
{
    public List<Transform> Transforms;  // 要测试的节点列表

    public DebugHullFlags HullDrawingOptions = DebugHullFlags.Outline;  // 凸包绘制选项

    [Header("可视化选项")]
    public bool DrawIsCollided;  // 绘制碰撞状态
    public bool DrawIntersection; // 绘制相交区域

    [Header("控制台日志")]
    public bool LogContact;       // 记录接触日志

    private Dictionary<int, TestShape> Hulls;  // 凸包字典(键:实例ID,值:TestShape测试形状)

    void Update()
    {
        HandleTransformChanged();  // 处理节点变化
        HandleHullCollisions();   // 处理凸包碰撞
    }

    // 处理凸包碰撞检测
    private void HandleHullCollisions()
    {
        for (int i = 0; i < Transforms.Count; ++i)
        {
            var tA = Transforms[i];
            if (tA == null)
                continue;

            // 获取凸包和节点信息
            var hullA = Hulls[tA.GetInstanceID()].Hull;
            //RigidTransform是Unity.Mathematics下的高性能结构体，就是为了之后的变换速度快，只关心位置角度这些信息
            var transformA = new RigidTransform(tA.rotation, tA.position);

            // 绘制凸包调试信息，主要就是外部轮廓
            HullDrawingUtility.DrawDebugHull(hullA, transformA, HullDrawingOptions);
            
            // 与其他物体的碰撞检测
            for (int j = i + 1; j < Transforms.Count; j++)
            {
                var tB = Transforms[j];
                if (tB == null)
                    continue;

                if (!tA.hasChanged && !tB.hasChanged)
                    continue;
                
                var hullB = Hulls[tB.GetInstanceID()].Hull;
                var transformB = new RigidTransform(tB.rotation, tB.position);
                //HullDrawingUtility.DrawDebugHull(hullB, transformB, HullDrawingOptions);

                // 绘制碰撞信息
                DrawHullCollision(tA.gameObject, tB.gameObject, transformA, hullA, transformB, hullB);
            }
            
        }
    }
    
    // 绘制凸包碰撞信息
    public void DrawHullCollision(GameObject a, GameObject b, RigidTransform t1, NativeHull hull1, RigidTransform t2, NativeHull hull2)
    {
        var collision = HullCollision.GetDebugCollisionInfo(t1, hull1, t2, hull2);
        if (collision.IsColliding)
        {
            // 绘制相交区域
            if (DrawIntersection)
            {
                HullIntersection.DrawNativeHullHullIntersection(t1, hull1, t2, hull2);              
            }

            // 绘制接触信息
            if (LogContact)
            {
                var sw1 = Stopwatch.StartNew();
                var tmp = new NativeManifold(Allocator.Persistent);
                var normalResult = HullIntersection.NativeHullHullContact(ref tmp, t1, hull1, t2, hull2);
                sw1.Stop();
                tmp.Dispose();

                var sw2 = Stopwatch.StartNew();
                var burstResult = HullOperations.TryGetContact.Invoke(out NativeManifold manifold, t1, hull1, t2, hull2);
                sw2.Stop();

                if(LogContact)
                {
                    Debug.Log($"'{a.name}'与'{b.name}'的接触计算耗时: {sw1.Elapsed.TotalMilliseconds:N4}ms (普通), {sw2.Elapsed.TotalMilliseconds:N4}ms (Burst)");
                }
            }

            // 绘制碰撞状态
            if(DrawIsCollided)
            {
                DebugDrawer.DrawSphere(t1.pos, 0.1f, UnityColors.GhostDodgerBlue);
                DebugDrawer.DrawSphere(t2.pos, 0.1f, UnityColors.GhostDodgerBlue);
            }
        }
    }

    // 处理变换变化
    private void HandleTransformChanged()
    {
        //Transforms.ToList()为了得到副本不影响原来的，Distinct去重一下，然后只保留激活状态的节点最后输出一个List出来
        //这里主要是为了测试方便，也就不写什么缓存来减少GC了
        var transforms = Transforms.ToList().Distinct().Where(t => t.gameObject.activeSelf).ToList();
        var newTransformFound = false;
        var transformCount = 0;

        //过滤下有没有新的物体，看看要不要重建
        if (Hulls != null)
        {
            for (var i = 0; i < transforms.Count; i++)
            {
                var t = transforms[i];
                if (t == null)
                    continue;

                transformCount++;

                var foundNewHull = !Hulls.ContainsKey(t.GetInstanceID());
                if (foundNewHull)
                {
                    newTransformFound = true;
                    break;
                }
            }

            if (!newTransformFound && transformCount == Hulls.Count)
                return;
        }

        //经历过上面的判断还能到这里就是要重建了
        Debug.Log("重建对象");

        //安全的释放资源
        EnsureDestroyed();

        //保存下不为空的节点，InstanceID作为key，创建的TestShape作为value记录在字典里，方便后续使用
        Hulls = transforms.Where(t => t != null).ToDictionary(k => k.GetInstanceID(), CreateShape);
        
        //Unity编辑器中的一个方法，强制重新绘制场景视图
        SceneView.RepaintAll();
    }

    // 创建测试形状
    private TestShape CreateShape(Transform t)
    {        
        var hull = CreateHull(t);
        
        return new TestShape
        {
            Id = t.GetInstanceID(),
            Hull = hull,
        };
    }

    // 根据变换创建凸包
    private NativeHull CreateHull(Transform v)
    {
        var collider = v.GetComponent<Collider>();
        if(collider is MeshCollider meshCollider)
        {
            return HullFactory.CreateFromMesh(meshCollider.sharedMesh);  // 从网格碰撞体创建凸包
        }
        var mf = v.GetComponent<MeshFilter>();
        if(mf != null && mf.sharedMesh != null)
        {
            return HullFactory.CreateFromMesh(mf.sharedMesh);  // 从网格过滤器创建凸包
        }
        throw new InvalidOperationException($"无法从游戏对象 '{v?.name}' 创建凸包");
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif
    }

#if UNITY_EDITOR
    // 编辑器播放模式状态变化回调
    private void EditorApplication_playModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
            case PlayModeStateChange.ExitingPlayMode:
                EnsureDestroyed();
                break;
        }
    }
#endif

    void OnDestroy() => EnsureDestroyed();
    void OnDisable() => EnsureDestroyed();

    // 确保资源被销毁
    private void EnsureDestroyed()
    {
        if (Hulls == null)
            return;

        foreach(var kvp in Hulls)
        {
            if (kvp.Value.Hull.IsValid)
            {
                kvp.Value.Hull.Dispose();
            }
        }
 
        Hulls.Clear();
    }
}