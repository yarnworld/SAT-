using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Common;

namespace UnityNativeHull
{
    public struct ClipVertex
    {
        public float3 position;
        public FeaturePair featurePair;
        public NativePlane plane;
        public float3 hull2local;
    };

    public struct ClipPlane
    {
        public Vector3 position;
        public NativePlane plane;
        public int edgeId;
    };

    public class HullIntersection
    {
        
        // 可视化两个 NativeHull（凸包）之间的相交区域
        // 返回值目前总是 true，仅用于调试用途（不做实际相交测试）
        public static bool DrawNativeHullHullIntersection(RigidTransform transform1, NativeHull hull1, RigidTransform transform2, NativeHull hull2)
        {
            // 对 hull2 的每一个面，与 hull1 做裁剪
            for (int i = 0; i < hull2.FaceCount; i++)
            {
                // 创建一个临时的接触面区域（manifold）用于存储裁剪结果
                var tmp = new NativeManifold(Allocator.Temp);

                // 将 hull2 的第 i 个面投影/裁剪到 hull1 上
                ClipFace(ref tmp, i, transform2, hull2, transform1, hull1);

                // 将裁剪出的接触区域绘制出来（调试用）
                HullDrawingUtility.DebugDrawManifold(tmp);

                // 释放临时资源
                tmp.Dispose();
            }

            // 对 hull1 的每一个面，与 hull2 做裁剪（同样的操作反向再做一次）
            for (int i = 0; i < hull1.FaceCount; i++)
            {
                var tmp = new NativeManifold(Allocator.Temp);

                // 将 hull1 的第 i 个面投影/裁剪到 hull2 上
                ClipFace(ref tmp, i, transform1, hull1, transform2, hull2);

                // 绘制结果
                HullDrawingUtility.DebugDrawManifold(tmp);

                tmp.Dispose();
            }

            // 返回值目前无意义，始终返回 true，仅作为调试函数
            return true;
        }


        // 执行一次面裁剪操作，将 hull1 的第 i 个面转换为裁剪平面，
        // 然后用 hull2 中与该平面“最对齐”的面进行裁剪操作。
        // 裁剪结果保存在 tmp 中。
        private static void ClipFace(ref NativeManifold tmp, int i, RigidTransform transform1, NativeHull hull1, RigidTransform transform2, NativeHull hull2)
        {
            // 将 hull1 的第 i 个局部平面变换到世界空间中
            NativePlane plane = transform1 * hull1.GetPlane(i);

            // 找出 hull2 中与该平面“最对齐”（法线方向最接近反向）的面索引，
            // 即：入射面（incident face），也可以理解为要被裁剪的面。
            var incidentFaceIndex = ComputeIncidentFaceIndex(plane, transform2, hull2);

            // 执行实际裁剪操作：
            // 将 hull2 的 incidentFaceIndex 面（世界空间）裁剪到 hull1 的第 i 个面（世界空间）上，
            // 得到两个凸包在该面方向上的交集部分（如有的话）。
            ClipFaceAgainstAnother(ref tmp, incidentFaceIndex, transform2, hull2, i, transform1, hull1);
        }


        // 将 hull2 的某个面（incidentFaceIndex）裁剪到 hull1 的 referenceFaceIndex 面所在的平面上
        // 输出裁剪后的结果到 output（NativeManifold）中
        // transform1 和 transform2 分别是 hull1 和 hull2 的世界变换
        public static int ClipFaceAgainstAnother(ref NativeManifold output, int referenceFaceIndex,
            RigidTransform transform1, NativeHull hull1, int incidentFaceIndex, RigidTransform transform2,
            NativeHull hull2)
        {
            // 确保 output 是已初始化的 NativeManifold
            Debug.Assert(output.IsCreated);

            // 获取 referenceFaceIndex 所在的局部平面，并转换为世界空间平面（裁剪参考平面）
            var refPlane = hull1.GetPlane(referenceFaceIndex);
            NativePlane referencePlane = transform1 * refPlane;

            // 创建一个临时 NativeBuffer，用于存储 hull1 中所有与 referenceFace 相关的裁剪平面（ClipPlane）
            NativeBuffer<ClipPlane> clippingPlanes = new NativeBuffer<ClipPlane>(hull1.FaceCount, Allocator.Temp);

            // 从 referenceFace 构建裁剪平面组，用于将 incident polygon 裁剪成相交区域
            GetClippingPlanes(ref clippingPlanes, transform1, hull1);

            // 创建并初始化 incident polygon，表示被裁剪的面（来自 hull2）
            NativeBuffer<ClipVertex> incidentPolygon = new NativeBuffer<ClipVertex>(hull1.VertexCount, Allocator.Temp);
            ComputeFaceClippingPolygon(ref incidentPolygon, incidentFaceIndex, transform2, hull2);

            // 将 incident polygon 裁剪到 referenceFace 的每一个边界平面上（Sutherland-Hodgman 多边形裁剪过程）
            for (int i = 0; i < clippingPlanes.Length; ++i)
            {
                // 临时缓冲区用于存放当前裁剪结果
                NativeBuffer<ClipVertex> outputPolygon =
                    new NativeBuffer<ClipVertex>(math.max(hull1.VertexCount, hull2.VertexCount), Allocator.Temp);

                // 执行一次平面对 incident polygon 的裁剪操作（类似半平面裁剪）
                Clip(clippingPlanes[i], ref incidentPolygon, ref outputPolygon);

                // 如果裁剪结果为空，表示完全被裁剪掉了，没有相交区域，提前返回
                if (outputPolygon.Length == 0)
                {
                    return -1;
                }

                // 释放旧的 incidentPolygon 缓存（不再需要）
                incidentPolygon.Dispose();

                // 使用当前裁剪结果作为下一轮裁剪的输入
                incidentPolygon = outputPolygon;
            }

            // 遍历裁剪完成的顶点，将它们添加到 output Manifold 中
            for (int i = 0; i < incidentPolygon.Length; ++i)
            {
                ClipVertex vertex = incidentPolygon[i];

                // 计算该点到参考平面的距离（用于后续碰撞深度判断等）
                float distance = referencePlane.Distance(vertex.position);

                // 将点加入结果 Manifold 中，同时保留特征对信息
                output.Add(vertex.position, distance, new ContactID { FeaturePair = vertex.featurePair });
            }

            // 清理临时内存
            clippingPlanes.Dispose();
            incidentPolygon.Dispose();

            // 返回被裁剪的面索引（通常用于记录或调试）
            return incidentFaceIndex;
        }

        /// <summary>
        /// 执行 Sutherland-Hodgman 多边形裁剪算法。
        /// 所有边界平面的法线都指向外侧，因此保留“位于平面后面”的顶点。
        /// </summary>
        public static void Clip(ClipPlane clipPlane, ref NativeBuffer<ClipVertex> input, ref NativeBuffer<ClipVertex> output)
        {
            // 确保输入是有效的并且非空
            Debug.Assert(input.IsCreated && input.Length != 0);

            // 从最后一个点开始（环形多边形的最后一条边）
            ClipVertex vertex1 = input[input.Length - 1];
            float distance1 = clipPlane.plane.Distance(vertex1.position); // 顶点1到裁剪平面的距离

            // 遍历多边形所有边（环形结构）
            for (int i = 0; i < input.Length; ++i)
            {
                ClipVertex vertex2 = input[i];
                float distance2 = clipPlane.plane.Distance(vertex2.position); // 顶点2到裁剪平面的距离

                if (distance1 <= 0 && distance2 <= 0)
                {
                    // 顶点1和顶点2都在平面后面或正好在平面上 → 保留顶点2
                    output.Add(vertex2);
                }
                else if (distance1 <= 0 && distance2 > 0)
                {
                    // 顶点1在平面后面，顶点2在平面前面 → 从“内”到“外”穿过平面 → 生成交点
                    float fraction = distance1 / (distance1 - distance2); // 计算插值比例
                    float3 position = vertex1.position + fraction * (vertex2.position - vertex1.position); // 插值得到交点位置

                    // 创建交点 ClipVertex，并设置特征对信息（feature pair）
                    ClipVertex vertex;
                    vertex.position = position;
                    vertex.featurePair.InEdge1 = -1;
                    vertex.featurePair.InEdge2 = vertex1.featurePair.OutEdge2;
                    vertex.featurePair.OutEdge1 = (sbyte)clipPlane.edgeId; // 交点由当前平面裁剪边产生
                    vertex.featurePair.OutEdge2 = -1;
                    vertex.plane = clipPlane.plane;
                    vertex.hull2local = position;
                    output.Add(vertex); // 添加交点
                }
                else if (distance2 <= 0 && distance1 > 0)
                {
                    // 顶点1在平面前面，顶点2在平面后面 → 从“外”到“内”穿过平面 → 生成交点，并保留顶点2
                    float fraction = distance1 / (distance1 - distance2); // 插值比例
                    float3 position = vertex1.position + fraction * (vertex2.position - vertex1.position); // 插值得到交点

                    ClipVertex vertex;
                    vertex.position = position;
                    vertex.featurePair.InEdge1 = (sbyte)clipPlane.edgeId; // 当前平面是交点进入边
                    vertex.featurePair.OutEdge1 = -1;
                    vertex.featurePair.InEdge2 = -1;
                    vertex.featurePair.OutEdge2 = vertex1.featurePair.OutEdge2;
                    vertex.plane = clipPlane.plane;
                    vertex.hull2local = position;
                    output.Add(vertex); // 添加交点

                    output.Add(vertex2); // 添加顶点2
                }

                // 移动到下一条边
                vertex1 = vertex2;
                distance1 = distance2;
            }
        }

        /// <summary>
        /// 在另一个凸包中查找与参考平面“最不平行”的面索引，
        /// 用于选出“incident face”（将被裁剪的面）
        /// </summary>
        public static unsafe int ComputeIncidentFaceIndex(NativePlane facePlane, RigidTransform transform, NativeHull hull)
        {
            // 初始化，默认先取第一个面作为最不平行的候选
            int faceIndex = 0;

            // 计算参考面法线与第一个面的夹角余弦值（dot 越接近 1 越平行）
            float min = math.dot(facePlane.Normal, (transform * hull.GetPlane(faceIndex)).Normal);

            // 遍历所有面，寻找 dot 值最小的（也就是法线方向与参考面最不一致的）
            for (int i = 1; i < hull.FaceCount; ++i)
            {
                float dot = math.dot(facePlane.Normal, (transform * hull.GetPlane(i)).Normal);

                if (dot < min)
                {
                    // 找到新的最小 dot，即找到更“不平行”的面
                    min = dot;
                    faceIndex = i;
                }
            }

            // 返回该面索引，它将作为“incident face”
            return faceIndex;
        }


        /// <summary>
        /// 获取所有变换后的裁剪面（clipping planes），用于将 incident 面裁剪到 reference 面的边界内。
        /// </summary>
        public static unsafe void GetClippingPlanes(ref NativeBuffer<ClipPlane> output,  RigidTransform transform, NativeHull hull)
        {
            Debug.Assert(output.IsCreated); // 确保输出缓冲区已初始化

            for (int i = 0; i < hull.FaceCount; i++)
            {
                var p = hull.GetPlane(i); // 获取第 i 个面的局部平面信息

                // 将平面从局部空间转换到世界空间或参考空间
                output.Add(new ClipPlane
                {
                    plane = transform * p,
                });
            }
        }


        /// <summary>
        /// 为指定面生成其所有边对应的裁剪平面（side planes），并写入到输出列表中。
        /// </summary>
        public static unsafe void GetFaceSidePlanes(ref NativeBuffer<ClipPlane> output, NativePlane facePlane, int faceIndex, RigidTransform transform, NativeHull hull)
        { 
            // 获取该面起始边（HalfEdge结构）
            NativeHalfEdge* start = hull.GetEdgePtr(hull.GetFacePtr(faceIndex)->Edge);
            NativeHalfEdge* current = start;

            do
            {
                // 获取当前边的“对边”（另一面共享的边）
                NativeHalfEdge* twin = hull.GetEdgePtr(current->Twin);

                // 取当前边的两个端点（并转换到目标空间）
                float3 P = math.transform(transform, hull.GetVertex(current->Origin));
                float3 Q = math.transform(transform, hull.GetVertex(twin->Origin));

                // 构造裁剪平面（边 × 面法线 = 垂直于边并指向外侧的平面）
                ClipPlane clipPlane = default;
                clipPlane.edgeId = twin->Twin; // 记录边的 ID
                clipPlane.plane.Normal = math.normalize(math.cross(Q - P, facePlane.Normal)); // 侧平面法线
                clipPlane.plane.Offset = math.dot(clipPlane.plane.Normal, P); // 侧平面偏移量（点法式）

                // 添加裁剪平面到输出列表中
                output.Add(clipPlane);

                // 移动到下一个边
                current = hull.GetEdgePtr(current->Next);
            }
            while (current != start); // 遍历完整个环（一个面是一圈连通边）
        }


        /// <summary>
        /// 获取指定面的所有顶点并转换后写入输出，用于构建用于裁剪的凸多边形（即 incident face）。
        /// </summary>
        public static unsafe void ComputeFaceClippingPolygon(ref NativeBuffer<ClipVertex> output, int faceIndex, RigidTransform t, NativeHull hull)
        {
            Debug.Assert(output.IsCreated); // 确保输出缓冲区已创建

            // 获取面数据和该面所在的平面
            NativeFace* face = hull.GetFacePtr(faceIndex);
            NativePlane plane = hull.GetPlane(faceIndex);

            // 获取该面首条边
            NativeHalfEdge* start = hull.GetEdgePtr(face->Edge);
            NativeHalfEdge* current = start;

            do
            {
                NativeHalfEdge* twin = hull.GetEdgePtr(current->Twin);

                // 获取当前边的起点（局部空间）并转换到目标空间（例如世界空间）
                float3 vertex = hull.GetVertex(current->Origin);
                float3 P = math.transform(t, vertex);

                // 构造裁剪顶点结构
                ClipVertex clipVertex;
                clipVertex.featurePair.InEdge1 = -1;
                clipVertex.featurePair.OutEdge1 = -1;
                clipVertex.featurePair.InEdge2 = (sbyte)current->Next;     // 属于 incident hull
                clipVertex.featurePair.OutEdge2 = (sbyte)twin->Twin;       // 属于 reference hull
                clipVertex.position = P;             // 变换后的位置
                clipVertex.hull2local = vertex;      // 原始顶点坐标（未变换）
                clipVertex.plane = plane;            // 所属的平面

                // 添加到输出列表中
                output.Add(clipVertex);

                // 前进到下一个边
                current = hull.GetEdgePtr(current->Next);

            } while (current != start); // 遍历整个面的一圈边
        }

        //根据两个凸包上的对应边，计算它们的最近接触点和接触法线，生成接触信息（ContactPoint）并添加到碰撞清单（NativeManifold）中。
        public static unsafe void CreateEdgeContact(ref NativeManifold output, EdgeQueryResult input,
            RigidTransform transform1, NativeHull hull1, RigidTransform transform2, NativeHull hull2)
        {
            Debug.Assert(output.IsCreated); // 确保输出的碰撞信息缓冲区已创建

            ContactPoint cp = default; // 初始化一个空的接触点结构

            if (input.Index1 < 0 || input.Index2 < 0) // 若任何一个边索引无效则返回
                return;

            // 获取 hull1 中输入的边和它的对边（twin）
            NativeHalfEdge* edge1 = hull1.GetEdgePtr(input.Index1);
            NativeHalfEdge* twin1 = hull1.GetEdgePtr(edge1->Twin);

            // 获取边的两个端点，转换到世界空间
            float3 P1 = math.transform(transform1, hull1.GetVertex(edge1->Origin));
            float3 Q1 = math.transform(transform1, hull1.GetVertex(twin1->Origin));
            float3 E1 = Q1 - P1; // hull1 边向量

            // 同理，获取 hull2 中对应边和对边的端点（也转换到世界空间）
            NativeHalfEdge* edge2 = hull2.GetEdgePtr(input.Index2);
            NativeHalfEdge* twin2 = hull2.GetEdgePtr(edge2->Twin);

            float3 P2 = math.transform(transform1, hull2.GetVertex(edge2->Origin));
            float3 Q2 = math.transform(transform1, hull2.GetVertex(twin2->Origin));
            float3 E2 = Q2 - P2; // hull2 边向量

            // 计算两个边向量的法线（叉乘），代表潜在的碰撞法线方向
            float3 normal = math.normalize(math.cross(Q1 - P1, Q2 - P2));

            // 计算两个物体位置向量之差
            float3 C2C1 = transform2.pos - transform1.pos;

            // 判断法线方向是否指向 hull2 -> hull1，如果点积 < 0，需要反转法线方向及相关特征对
            if (math.dot(normal, C2C1) < 0)
            {
                // 翻转法线方向
                output.Normal = -normal;

                // 设置接触点的特征对索引（表示哪个边参与接触）
                cp.Id.FeaturePair.InEdge1 = input.Index2;
                cp.Id.FeaturePair.OutEdge1 = input.Index2 + 1;

                cp.Id.FeaturePair.InEdge2 = input.Index1 + 1;
                cp.Id.FeaturePair.OutEdge2 = input.Index1;
            }
            else
            {
                output.Normal = normal;

                cp.Id.FeaturePair.InEdge1 = input.Index1;
                cp.Id.FeaturePair.OutEdge1 = input.Index1 + 1;

                cp.Id.FeaturePair.InEdge2 = input.Index2 + 1;
                cp.Id.FeaturePair.OutEdge2 = input.Index2;
            }

            // 计算两个线段（边）之间的最近点对，得到最近的接触点 C1 和 C2
            ClosestPointsSegmentSegment(P1, Q1, P2, Q2, out float3 C1, out float3 C2);

            // 接触点位置是两个最近点的中点
            float3 position = 0.5f * (C1 + C2);

            // 设置接触点相关数据
            cp.Penetration = C1 - C2; // 穿透向量（两个最近点的向量差）
            cp.Position = position; // 接触点位置
            cp.Distance = input.Distance; // 距离（可能为负值表示穿透深度）

            // 将接触点添加到输出碰撞清单
            output.Add(cp);
        }

        //计算两条线段上最近的两点位置。该算法是求线段间的最近点对。
        public static void ClosestPointsSegmentSegment(float3 P1, float3 Q1, float3 P2, float3 Q2, out float3 C1, out float3 C2)
        {
            // 线段向量及点向量差
            float3 P2P1 = P1 - P2;
            float3 E1 = Q1 - P1;
            float3 E2 = Q2 - P2;

            // 计算向量的长度平方
            float D1 = math.lengthsq(E1);  // 边1长度平方
            float D2 = math.lengthsq(E2);  // 边2长度平方

            // 计算边向量的点积
            float D12 = math.dot(E1, E2);

            // 计算从 P2 到 P1 投影到边向量上的距离
            float DE1P1 = math.dot(E1, P2P1);
            float DE2P1 = math.dot(E2, P2P1);

            // 计算分母，避免除零
            float DNM = D1 * D2 - D12 * D12;

            // 计算两个参数（F1 和 F2）表示在各自边上的最近点位置的比例因子
            float F1 = (D12 * DE2P1 - DE1P1 * D2) / DNM;
            float F2 = (D12 * F1 + DE2P1) / D2;

            // 计算两个最近点
            C1 = P1 + F1 * E1;
            C2 = P2 + F2 * E2;
        }

        public static bool NativeHullHullContact(ref NativeManifold result, RigidTransform transform1, NativeHull hull1, RigidTransform transform2, NativeHull hull2)
        {
            // 查询第一个凸包 hull1 在 hull2 上的面距离（用于碰撞检测中的面距离判定）
            FaceQueryResult faceQuery1;
            HullCollision.QueryFaceDistance(out faceQuery1, transform1, hull1, transform2, hull2);

            // 如果第一个面的距离大于0，表示两凸包在该面方向上未接触（分离）
            if (faceQuery1.Distance > 0)
            {
                return false; // 无碰撞
            }

            // 同理，查询第二个凸包 hull2 在 hull1 上的面距离
            FaceQueryResult faceQuery2;
            HullCollision.QueryFaceDistance(out faceQuery2, transform2, hull2, transform1, hull1);

            // 若第二个面的距离大于0，也表示分离无碰撞
            if (faceQuery2.Distance > 0)
            {
                return false; // 无碰撞
            }

            // 查询两个凸包之间的边距离（边与边的最近距离），边检测有助于发现边缘接触情况
            HullCollision.QueryEdgeDistance(out EdgeQueryResult edgeQuery, transform1, hull1, transform2, hull2);

            // 如果边距离大于0，说明边之间也没有接触，返回无碰撞
            if (edgeQuery.Distance > 0)
            {
                return false;
            }

            // 以下是容差参数，决定在接触判定中容许的误差范围
            float kRelEdgeTolerance = 0.90f; // 边检测相对容忍度90%
            float kRelFaceTolerance = 0.95f; // 面检测相对容忍度95%
            float kAbsTolerance = 0.5f * 0.005f; // 绝对容忍度，控制最小容忍范围

            // 选出两个面距离的最大值，用于后续和边距离比较
            float maxFaceSeparation = math.max(faceQuery1.Distance, faceQuery2.Distance);

            // 如果边距离远大于（面距离 * 90% + 绝对容差），则认为边接触更可信
            if (edgeQuery.Distance > kRelEdgeTolerance * maxFaceSeparation + kAbsTolerance)
            {
                // 生成边接触信息，添加到碰撞结果中
                CreateEdgeContact(ref result, edgeQuery, transform1, hull1, transform2, hull2);
            }
            else
            {
                // 否则更倾向于生成面接触信息

                // 这里为避免面接触翻转（flip-flop）问题，偏好第一个凸包的面作为参考面
                // 如果第二个面距离明显大于第一个面距离的95% + 绝对容差
                if (faceQuery2.Distance > kRelFaceTolerance * faceQuery1.Distance + kAbsTolerance)
                {
                    // 以 hull2 的面为参考面，hull1 的面为 incident（入射面）
                    CreateFaceContact(ref result, faceQuery2, transform2, hull2, transform1, hull1, true);
                }
                else
                {
                    // 否则以 hull1 的面为参考面，hull2 的面为 incident
                    CreateFaceContact(ref result, faceQuery1, transform1, hull1, transform2, hull2, false);
                }
            }

            return true; // 碰撞检测通过，已生成接触信息
        }


        //获取参考面及其侧边平面：用于作为裁剪的基准，限制入射多边形范围。
        //计算入射面多边形：选取与参考面法线最不平行的另一 hull 面，得到入射面多边形。
        //多边形裁剪：使用参考面的所有侧边平面对入射多边形逐一裁剪，最终得到被限制在参考面范围内的多边形。
        //生成接触点：取裁剪后的多边形顶点中，在参考面之下的点，作为接触点。
        //处理法线翻转：若碰撞方向需要翻转，法线和特征对索引也同步翻转，保证碰撞结果正确。
        //结果保存：把每个有效接触点添加到输出接触点集合 output 中。
        public unsafe static void CreateFaceContact(ref NativeManifold output, FaceQueryResult input, RigidTransform transform1, NativeHull hull1, RigidTransform transform2, NativeHull hull2, bool flipNormal)
        {
            // 获取参考凸包（hull1）中碰撞面索引对应的平面信息
            var refPlane = hull1.GetPlane(input.Index);
            // 将参考平面转换到世界空间
            NativePlane referencePlane = transform1 * refPlane;

            // 在栈上分配一个存储裁剪平面的缓冲区，大小为 hull1 的面数量
            var clippingPlanesStackPtr = stackalloc ClipPlane[hull1.FaceCount];
            var clippingPlanes = new NativeBuffer<ClipPlane>(clippingPlanesStackPtr, hull1.FaceCount);
            
            // 下面两行是注释的备选方案，使用托管堆分配的 NativeList
            // NativeList<ClipPlane> clippingPlanes = new NativeList<ClipPlane>((int)hull1.FaceCount, Allocator.Temp);

            // 获取参考面的侧边平面，这些平面用于裁剪入射面多边形
            GetFaceSidePlanes(ref clippingPlanes, referencePlane, input.Index, transform1, hull1);

            // 在栈上分配一个缓冲区，用来存储入射多边形顶点，大小为 hull1 的顶点数
            var incidentPolygonStackPtr = stackalloc ClipVertex[hull1.FaceCount];
            var incidentPolygon = new NativeBuffer<ClipVertex>(incidentPolygonStackPtr, hull1.VertexCount);

            // 计算入射面索引（即与参考面法线最不平行的 hull2 的面）
            var incidentFaceIndex = ComputeIncidentFaceIndex(referencePlane, transform2, hull2);
            // 计算入射面多边形的顶点（裁剪多边形的初始顶点）
            ComputeFaceClippingPolygon(ref incidentPolygon, incidentFaceIndex, transform2, hull2);

            // 以下是用于多边形裁剪的临时输出缓冲区，大小同样为 hull1.FaceCount
            var outputPolygonStackPtr = stackalloc ClipVertex[hull1.FaceCount];

            // 使用参考面的侧边平面逐个裁剪入射多边形
            for (int i = 0; i < clippingPlanes.Length; ++i)
            {
                // 每次裁剪产生新的输出多边形缓冲区
                var outputPolygon = new NativeBuffer<ClipVertex>(outputPolygonStackPtr, hull1.FaceCount);

                // 将入射多边形按当前裁剪平面裁剪，结果存到 outputPolygon
                Clip(clippingPlanes[i], ref incidentPolygon, ref outputPolygon);

                // 如果裁剪后多边形顶点数为0，说明无交集，提前返回
                if (outputPolygon.Length == 0)
                {
                    return;
                }
                
                // 准备下一次裁剪，incidentPolygon 指向最新的裁剪结果
                incidentPolygon = outputPolygon;
            }

            // 遍历裁剪后的入射多边形顶点，生成接触点
            for (int i = 0; i < incidentPolygon.Length; ++i)
            {
                ClipVertex vertex = incidentPolygon[i];
                // 计算顶点到参考平面的距离
                float distance = referencePlane.Distance(vertex.position);

                // 如果顶点在参考平面下方（或在平面上），则为有效接触点
                if (distance <= 0)
                {
                    ContactID id = default;
                    id.FeaturePair = vertex.featurePair; // 保存特征对信息（边/顶点标识）

                    if (flipNormal)
                    {
                        // 如果需要翻转法线，法线取反
                        output.Normal = -referencePlane.Normal;
                        // 同时交换特征对中边的索引，保持一致性
                        Swap(id.FeaturePair.InEdge1, id.FeaturePair.InEdge2);
                        Swap(id.FeaturePair.OutEdge1, id.FeaturePair.OutEdge2);
                    }
                    else
                    {
                        // 否则法线保持参考平面法线方向
                        output.Normal = referencePlane.Normal;                        
                    }
                    
                    // 将顶点投影到参考平面上，作为接触点位置
                    float3 position = referencePlane.ClosestPoint(vertex.position);

                    // 将接触点位置、距离和特征对添加到碰撞接触点列表中
                    output.Add(position, distance, id);
                }
            }

            // 注意：clippingPlanes 和 incidentPolygon 都是栈上分配，没调用 Dispose
            // 若使用 NativeList，需显式 Dispose 释放托管内存
        }

        public static void Swap<T>(T a, T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

    }
}