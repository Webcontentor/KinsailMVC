﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace KinsailMVC.Models
{
    public class LocationRepository 
    {
        private List<Location> locations = new List<Location>();
        private int _nextId = 1;

        public LocationRepository()
        {
            
        }

        public IEnumerable<DataAccess.Location> GetAll()
        {
            DataAccess.DataClassesDataContext objData = new DataAccess.DataClassesDataContext();
            System.Data.Linq.Table<DataAccess.Location> objTest = objData.GetTable<DataAccess.Location>();
            return objTest.AsEnumerable<DataAccess.Location>();    
    
        }



        public Location Get(int id)
        {
            return locations.Find(p => p.LocationID == id);
        }

        public Location Add(Location item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            item.LocationID = _nextId++;
            locations.Add(item);
            return item;
        }

        public void Remove(int id)
        {
            locations.RemoveAll(p => p.LocationID == id);
        }

        public bool Update(Location item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            int index = locations.FindIndex(p => p.LocationID == item.LocationID);
            if (index == -1)
            {
                return false;
            }
            locations.RemoveAt(index);
            locations.Add(item);
            return true;
        }
    }
    
}