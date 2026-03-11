using UnityEngine;

namespace Common
{
    /// <summary>
    /// UnityColors 静态类，封装了一些常用的颜色及扩展方法
    /// </summary>
    public static class UnityColors
    {
        /// <summary>
        /// 扩展方法：设置颜色的不透明度（Alpha值）
        /// </summary>
        /// <param name="color">原始颜色</param>
        /// <param name="alpha">目标透明度</param>
        /// <returns>带有指定透明度的新颜色</returns>
        public static Color ToOpacity(this Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // Unity 默认颜色（只读属性）
        public static Color Blue { get; } = Color.blue;   // 蓝色
        public static Color Yellow { get; } = Color.yellow; // 黄色

        /// <summary>
        /// 自定义颜色：GhostDodgerBlue，带有一定透明度
        /// RGB值为(30,144,255)，透明度为0.65
        /// </summary>
        public static Color GhostDodgerBlue { get; } = new Color(30 / 255f, 144 / 255f, 255 / 255f, 0.65f);
    }
}