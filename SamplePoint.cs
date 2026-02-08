using System;

namespace _2._6test
{
    /// <summary>
    /// 数据采样点
    /// </summary>
    public class SamplePoint
    {
        /// <summary>
        /// 采样时间
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// 温度（°C）
        /// </summary>
        public double Temperature { get; set; }

        /// <summary>
        /// 湿度（%）
        /// </summary>
        public double Humidity { get; set; }
    }
}
