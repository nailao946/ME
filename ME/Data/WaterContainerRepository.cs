using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    /// <summary>
    /// 喝水容器仓库（water_containers.json），沿用 JsonStore 模式。
    /// </summary>
    public class WaterContainerRepository
    {
        private const string FileName = "water_containers";

        public List<WaterContainer> GetAll()
        {
            return JsonStore.Load<WaterContainer>(FileName).ToList();
        }

        public int Insert(WaterContainer container)
        {
            var items = JsonStore.Load<WaterContainer>(FileName);
            var maxId = items.Count > 0 ? items.Max(c => c.Id) : 0;
            container.Id = maxId + 1;
            items.Add(container);
            JsonStore.Save(FileName, items);
            return container.Id;
        }

        public void Update(WaterContainer container)
        {
            var items = JsonStore.Load<WaterContainer>(FileName);
            var existing = items.FirstOrDefault(c => c.Id == container.Id);
            if (existing != null)
            {
                existing.Name = container.Name;
                existing.CapacityMl = container.CapacityMl;
                JsonStore.Save(FileName, items);
            }
        }

        public void Delete(int id)
        {
            var items = JsonStore.Load<WaterContainer>(FileName);
            var target = items.FirstOrDefault(c => c.Id == id);
            if (target != null)
            {
                items.Remove(target);
                JsonStore.Save(FileName, items);
            }
        }

        /// <summary>确保至少有一个默认容器，返回全部容器</summary>
        public List<WaterContainer> EnsureDefaults()
        {
            var items = GetAll();
            if (items.Count == 0)
            {
                items = new List<WaterContainer>
                {
                    new WaterContainer { Name = "小杯", CapacityMl = 200, IsBuiltIn = true },
                    new WaterContainer { Name = "大杯", CapacityMl = 500, IsBuiltIn = true },
                    new WaterContainer { Name = "水壶", CapacityMl = 1000, IsBuiltIn = true }
                };
                for (int i = 0; i < items.Count; i++)
                    items[i].Id = i + 1;
                JsonStore.Save(FileName, items);
            }
            return items;
        }
    }
}
