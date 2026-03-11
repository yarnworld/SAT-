using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Common
{
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// 一个可以在 <see cref="DebugDrawer"/> 中绘制的结构体接口
    /// </summary>
    public interface IDebugDrawing
    {
        /// <summary>
        /// 绘制调试信息；由 <see cref="DebugDrawer"/> 在 SceneGUI 上下文中调用。
        /// </summary>
        void Draw();
    }

    /// <summary>
    /// 一个用于允许绘制 'Handles' 和 GUI 内容（如标签）的工具
    /// 可以在不受 Monobehavior/Editor OnGUI 方法限制的情况下使用。
    /// </summary>
    public static class DebugDrawer
    {
        public static Color DefaultColor = Color.white;  // 默认颜色为白色

        private static List<IDebugDrawing> Drawings = new List<IDebugDrawing>();  // 存储需要绘制的调试对象

#if UNITY_EDITOR

        static DebugDrawer()
        {
            // 在编辑器中订阅 SceneView 的绘制事件
            SceneView.duringSceneGui += SceneViewOnDuringSceneGui;
        }

        private static void SceneViewOnDuringSceneGui(SceneView obj)
        {
            // 使用绘图范围的作用域，确保绘制内容只在 SceneView 上显示
            using (var scope = new Handles.DrawingScope())
            {
                foreach (var drawing in Drawings)
                {
                    drawing.Draw();  // 绘制每个调试对象
                }
            }
            CheckForFrameChange();  // 检查是否需要清除旧的绘制对象
        }

        private static int _lastFrame;  // 记录上次的帧数
#endif

        private static void CheckForFrameChange()
        {
#if UNITY_EDITOR
            // SceneGui 和 Monobehavior 的更新时序不同
            // 因此需要在每一帧之间重新绘制元素。
            var t = Time.frameCount;
            if (_lastFrame != t)
            {
                Drawings.Clear();  // 清空之前的绘制列表
                _lastFrame = t;  // 更新帧数
            }
#endif
        }

        /// <summary>
        /// 在场景视图中绘制自定义的调试内容
        /// </summary>
        /// <param name="drawing">实现了 IDebugDrawing 接口的实例</param>
        [Conditional("UNITY_EDITOR")]
        public static void Draw(IDebugDrawing drawing)
        {
            CheckForFrameChange();
            Drawings.Add(drawing);  // 将绘制内容添加到列表
        }

        /// <summary>
        /// 在 3D 空间中绘制文本标签
        /// </summary>
        /// <param name="position">标签在世界坐标中的位置</param>
        /// <param name="text">标签显示的文本</param>
        /// <param name="style">控制标签外观的样式</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawLabel(Vector3 position, string text, GUIStyle style = null)
        {
            Draw(new LabelDrawing
            {
                Position = position,
                Text = text,
                Style = style,
            });
        }
        
        /// <summary>
        /// 绘制反走样的凸多边形
        /// </summary>
        /// <param name="verts">描述凸多边形的点的列表</param>
        /// <param name="faceColor">多边形的颜色</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawAAConvexPolygon(Vector3[] verts, Color? color = null)
        {
            Draw(new PolygonDrawing
            {
                Color = color ?? DefaultColor,
                Verts = verts,
            });
        }

        /// <summary>
        /// 绘制球体
        /// </summary>
        /// <param name="center">球体的中心位置</param>
        /// <param name="radius">球体的半径</param>
        /// <param name="color">球体的颜色</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawSphere(Vector3 center, float radius, Color? color = null)
        {
            Draw(new SphereDrawing
            {
                Color = color ?? DefaultColor,
                Center = center,
                Radius = radius,
            });
        }

        /// <summary>
        /// 绘制箭头
        /// </summary>
        /// <param name="position">箭头的起始位置</param>
        /// <param name="direction">箭头的指向方向</param>
        /// <param name="color">箭头的颜色</param>
        /// <param name="duration">箭头绘制的持续时间</param>
        /// <param name="depthTest">箭头是否被遮挡时透明</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawArrow(Vector3 position, Vector3 direction, Color? color = null, float duration = 0, bool depthTest = true)
        {
            Debug.DrawRay(position, direction, color ?? DefaultColor, duration, depthTest);  // 绘制箭头的线段
            DrawCone(position + direction, -direction * 0.333f, color ?? DefaultColor, 15, duration, depthTest);  // 绘制箭头的尾部
        }
        
        /// <summary>
        /// 绘制一个圆
        /// </summary>
        /// <param name="position">圆心的世界坐标</param>
        /// <param name="up">圆的法向量</param>
        /// <param name="radius">圆的半径</param>
        /// <param name="color">圆的颜色</param>
        /// <param name="duration">圆的绘制持续时间</param>
        /// <param name="depthTest">是否深度检测圆的显示</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawCircle(Vector3 position, Vector3 up, float radius = 1.0f, Color? color = null, float duration = 0, bool depthTest = true)
        {
            Vector3 _up = up.normalized * radius;
            Vector3 _forward = Vector3.Slerp(_up, -_up, 0.5f);
            Vector3 _right = Vector3.Cross(_up, _forward).normalized * radius;

            Matrix4x4 matrix = new Matrix4x4();

            matrix[0] = _right.x;
            matrix[1] = _right.y;
            matrix[2] = _right.z;

            matrix[4] = _up.x;
            matrix[5] = _up.y;
            matrix[6] = _up.z;

            matrix[8] = _forward.x;
            matrix[9] = _forward.y;
            matrix[10] = _forward.z;

            Vector3 _lastPoint = position + matrix.MultiplyPoint3x4(new Vector3(Mathf.Cos(0), 0, Mathf.Sin(0)));
            Vector3 _nextPoint = Vector3.zero;

            // 画圆的每个点并连接
            for (var i = 0; i < 91; i++)
            {
                _nextPoint.x = Mathf.Cos((i * 4) * Mathf.Deg2Rad);
                _nextPoint.z = Mathf.Sin((i * 4) * Mathf.Deg2Rad);
                _nextPoint.y = 0;

                _nextPoint = position + matrix.MultiplyPoint3x4(_nextPoint);

                Debug.DrawLine(_lastPoint, _nextPoint, color ?? DefaultColor, duration, depthTest);
                _lastPoint = _nextPoint;
            }
        }

        /// <summary>
        /// 绘制一个圆锥
        /// </summary>
        /// <param name="position">圆锥的顶点位置</param>
        /// <param name="direction">圆锥扩展的方向</param>
        /// <param name="color">圆锥的颜色</param>
        /// <param name="angle">圆锥的角度</param>
        /// <param name="duration">圆锥的绘制持续时间</param>
        /// <param name="depthTest">是否深度检测圆锥</param>
        [Conditional("UNITY_EDITOR")]
        public static void DrawCone(Vector3 position, Vector3 direction, Color color = default, float angle = 45, float duration = 0, bool depthTest = true)
        {
            float length = direction.magnitude;

            Vector3 _forward = direction;
            Vector3 _up = Vector3.Slerp(_forward, -_forward, 0.5f);
            Vector3 _right = Vector3.Cross(_forward, _up).normalized * length;

            direction = direction.normalized;

            Vector3 slerpedVector = Vector3.Slerp(_forward, _up, angle / 90.0f);

            float dist;
            var farPlane = new Plane(-direction, position + _forward);
            var distRay = new Ray(position, slerpedVector);

            farPlane.Raycast(distRay, out dist);

            color = color != default ? color : Color.white;
            Debug.DrawRay(position, slerpedVector.normalized * dist, color);  // 绘制圆锥的第一条边
            Debug.DrawRay(position, Vector3.Slerp(_forward, -_up, angle / 90.0f).normalized * dist, color, duration, depthTest);  // 绘制第二条边
            Debug.DrawRay(position, Vector3.Slerp(_forward, _right, angle / 90.0f).normalized * dist, color, duration, depthTest);  // 绘制第三条边
            Debug.DrawRay(position, Vector3.Slerp(_forward, -_right, angle / 90.0f).normalized * dist, color, duration, depthTest);  // 绘制第四条边

            DrawCircle(position + _forward, direction, (_forward - (slerpedVector.normalized * dist)).magnitude, color, duration, depthTest);  // 绘制圆锥底部的圆
            DrawCircle(position + (_forward * 0.5f), direction, ((_forward * 0.5f) - (slerpedVector.normalized * (dist * 0.5f))).magnitude, color, duration, depthTest);  // 绘制圆锥中间的圆
        }
    }

    // 各种绘制结构体：用于不同类型的调试图形
    public struct SphereDrawing : IDebugDrawing
    {
        public Color Color;
        public float Radius;
        public Vector3 Center;

        public void Draw()
        {
#if UNITY_EDITOR
            Handles.color = Color;
            Handles.SphereHandleCap(0, Center, Quaternion.identity, Radius, EventType.Repaint);
#endif
        }
    }
    
    public struct PolygonDrawing : IDebugDrawing
    {
        public Color Color;
        public Vector3[] Verts;

        public void Draw()
        {
#if UNITY_EDITOR
            Handles.color = Color;
            Handles.DrawAAConvexPolygon(Verts);  // 绘制反走样的凸多边形
#endif
        }
    }
    
    public struct LabelDrawing : IDebugDrawing
    {
        public Vector3 Position;
        public string Text;
        public GUIStyle Style;

        public void Draw()
        {
            CenteredLabel(Position, Text, Style ?? SceneBoldLabelWithBackground.Value);
        }

        private static void CenteredLabel(Vector3 position, string text, GUIStyle style)
        {
#if UNITY_EDITOR
            try
            {
                GUIContent gUIContent = TempGuiContent(text, null, null);
                if (HandleUtility.WorldToGUIPointWithDepth(position).z < 0.0)
                    return;

                var size = style.CalcSize(gUIContent) / 2;
                Handles.BeginGUI();
                var screenPos = HandleUtility.WorldPointToSizedRect(position, gUIContent, style);
                screenPos.x -= size.x;
                screenPos.y -= size.y;
                GUI.Label(screenPos, gUIContent, style);  // 绘制标签
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
            finally
            {
                Handles.EndGUI();
            }
#endif
        }


        public static Lazy<GUIStyle> SceneBoldLabelWithBackground { get; } = new Lazy<GUIStyle>(() =>
        {
#if UNITY_EDITOR
            GUIStyle style = new GUIStyle(EditorStyles.helpBox);
#else
            GUIStyle style = new GUIStyle();
#endif
            style.contentOffset = new Vector2(2, 2);
            style.padding = new RectOffset(2, 2, 2, 2);
            style.normal.textColor = Color.black;
            return style;
        });
        
        private static GUIContent _guiContent = null;
        private static GUIContent TempGuiContent(string label, string tooltip = null, Texture2D icon = null)
        {
            if (_guiContent == null)
            {
                _guiContent = new GUIContent();
            }
            _guiContent.text = label;
            _guiContent.tooltip = tooltip;
            _guiContent.image = icon;
            return _guiContent;
        }
    }
}
