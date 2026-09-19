namespace ME.Models
{
    /// <summary>
    /// 喝水容器，存于 water_containers.json。
    /// 用于喝水记录按容器 ml 累加（不再按"杯"）。
    /// </summary>
    public class WaterContainer
    {
        public int Id { get; set; }

        /// <summary>容器名称，如"小杯""水壶"</summary>
        public string Name { get; set; }

        /// <summary>容量 ml</summary>
        public double CapacityMl { get; set; }

        public bool IsBuiltIn { get; set; }

        /// <summary>跨设备合并用全局唯一标识（旧数据无此字段，保存/合并时自动补）</summary>
        public string Uid { get; set; }

        public override string ToString() => $"{Name}（{CapacityMl:0} ml）";
    }
}
