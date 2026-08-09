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
    }
}
