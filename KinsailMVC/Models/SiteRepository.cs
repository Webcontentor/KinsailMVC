﻿using NPoco;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Http;

namespace KinsailMVC.Models
{
    // Database repository handler for sites
    //
    // TODO: refactor db connection as a factory?
    public class SiteRepository
    {
        private IDatabase db;
        private long siteItemTypeId = 0;
        private long locationItemTypeId = 0;
        private long galleryImageTypeId = 0;

        private long siteTypeFeatureId = 0;
        private long reservableFeatureId = 0;

        private Dictionary<string, SqlCriteria> allFeatures = new Dictionary<string, SqlCriteria>();
        private static string br = Environment.NewLine;


        // SQL query for list of all features
        private static string queryAllFeatures =
            "SELECT LOWER(f.Abbreviation) AS Name, f.FeatureID, ft.Category" + br +
            "  FROM Features f" + br +
            "  JOIN FeatureTypes ft ON f.FeatureTypeID = ft.FeatureTypeID";

        
        // -- Sites --------------------

        // SQL SELECT fragment for SiteBasic
        private static string selectSiteBasic =
            "SELECT i.ItemID, i.Name, i.Description, l.ItemID AS LocationID, f0.Value AS Type," + br +
            "       ixm.CoordinateX AS X, ixm.CoordinateY AS Y, g.ImageID, g.IconURL AS thumbUrl, g.FullURL";

        // SQL SELECT fragment for SiteDetail
        private static string selectSiteDetail =
            "SELECT i.ItemID, i.Name, i.Description, l.ItemID AS LocationID, f0.Value AS Type, f1.Value AS Reservable," + br +
            "       av.MaxAccommodatingUnits, av.MinDuration, av.MaxDuration, av.AdvancedReservationPeriod," + br +
            "       ixm.CoordinateX AS X, ixm.CoordinateY AS Y, g.ImageID, g.IconURL AS thumbUrl, g.FullURL";

        // SQL FROM/JOIN fragment for SiteBasic
        private static string fromJoinSiteBasic =
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXItems ixi ON i.ItemID = ixi.ItemID" + br +             // location
            "  LEFT OUTER JOIN Items l ON ixi.ParentItemID = l.ItemID" + br +
            "  LEFT OUTER JOIN (SELECT ItemID, Value" + br +                                // site type property (stored as a feature)
            "                     FROM ItemsXFeatures" + br +
            "                    WHERE FeatureID = @0) f0 ON i.ItemID = f0.ItemID" + br +
            "  LEFT OUTER JOIN ItemsXMaps ixm ON i.ItemID = ixm.ItemID" + br +              // maps
            "  LEFT OUTER JOIN Maps m ON ixm.MapID = m.MapID" + br +
            "  LEFT OUTER JOIN ItemsXFirstGalleryImage ixg ON l.ItemID = ixg.ItemID" + br + // first gallery image 
            "  LEFT OUTER JOIN Images g ON g.ImageID = ixg.ImageID";

        // SQL FROM/JOIN fragment for SiteDetail
        private static string fromJoinSiteDetail = fromJoinSiteBasic + br +
            "  LEFT OUTER JOIN (SELECT ItemID, Value" + br +                                // reservable property (stored as a feature)
            "                     FROM ItemsXFeatures" + br +
            "                    WHERE FeatureID = @1) f1 ON i.ItemID = f1.ItemID" + br +
            "  LEFT OUTER JOIN (SELECT ixar.ItemID, MIN(ixar.MaxUnits) AS MaxAccommodatingUnits," + br +  // availability info
            "                          MIN(a.MinDurationDays) AS MinDuration, MAX(MaxDurationDays) AS MaxDuration," + br +
            "                          MIN(a.AvailBeforeDays) AS AdvancedReservationPeriod" + br +
            "                     FROM ItemsXAvailRates ixar" + br +
            "                     LEFT OUTER JOIN Availability a ON ixar.AvailID = a.AvailID" + br +
            "                    GROUP BY ixar.ItemID" + br +
            "                   ) av ON i.ItemID = av.ItemID";

        // SQL WHERE fragments for Site Features
        private static string andWhereSiteHasFeatures_pre =
            "   AND i.ItemID IN (SELECT i.ItemID" + br +
            "                      FROM Items i" + br +
            "                      JOIN ItemsXFeatures ixf ON i.ItemID = ixf.ItemID" + br +
            "                      JOIN Features f ON f.FeatureID = ixf.FeatureID" + br +
            "                     WHERE ItemTypeID = {0}" + br +
            "                       AND ( " + br;
        private static string andWhereSiteFeature =
            "                             (ixf.FeatureID = {0} AND ixf.Value {1})" + br;
        private static string andWhereSiteHasFeatures_post =
            "                           )" + br +
            "                     GROUP BY i.ItemID" + br +
            "                    HAVING COUNT(f.FeatureID) >= {0})" + br;

        // SQL WHERE fragments for Site Reservations (prefix)
        private static string andWhereSiteReserved_pre =
            "   AND i.ItemID NOT IN (SELECT i.ItemID" + br +
            "                          FROM Items i" + br +
            "                          JOIN ReservationResources rr ON i.ItemID = rr.ItemID" + br +
            "                         WHERE i.ItemTypeID = {0}" + br;
        private static string andWhereSiteReserved_post =
            "                         GROUP BY i.ItemID, i.Name)" + br;

        // SQL WHERE fragment for Sites
        private static string whereSites =
            " WHERE i.ItemTypeID = @0" + br +
            "   AND l.ItemTypeID = @1";

        // SQL WHERE/ORDER fragment for Sites (by location)
        private static string whereSitesForLocation =
            " WHERE i.ItemTypeID = @0" + br +
            "   AND l.ItemTypeID = @1" + br +
            "   AND l.ItemID = @2";

        // SQL WHERE fragment for Sites (by ID)
        private static string whereSiteById =
            " WHERE i.ItemTypeID = @0" + br +
            "   AND l.ItemTypeID = @1" + br +
            "   AND i.ItemID = @2";

        // SQL ORDER fragment for Sites
        private static string orderSites =
            " ORDER BY i.Name";


        // -- Site Features --------------------

        // SQL query for list of site features (by siteId)
        private static string queryFeaturesById =
            "SELECT ixf.ID AS id, f.Abbreviation AS name, f.Name AS label, f.Description AS description, ixf.Value AS value," + br +
            "       dbo.fGetFeatureBullet(f.Name, f.NameNegative, ft.Name, ixf.Value) AS Bullet" + br +
            "  FROM ItemsXFeatures ixf" + br +
            "  JOIN Features f ON ixf.FeatureID = f.FeatureID" + br +
            "  JOIN FeatureTypes ft ON f.FeatureTypeID = ft.FeatureTypeID" + br +
            " WHERE f.FeatureID <> @0" + br +  // feature to exclude from results
            "   AND ixf.ItemID = @1" + br +
            "   AND f.Active = 1" + br +
            "   AND f.Hidden = 0" + br +
            " ORDER BY ixf.DisplayOrder";

        // SQL query for list of site features (for ALL sites)
        private static string querySiteFeatures =
            "SELECT i.ItemID AS siteId, ixf.ID AS id, f.Abbreviation AS name, f.Name AS label, f.Description AS description, ixf.Value AS value," + br +
            "       dbo.fGetFeatureBullet(f.Name, f.NameNegative, ft.Name, ixf.Value) AS Bullet" + br +
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXFeatures ixf ON ixf.ItemID = i.ItemID" + br +
            "  JOIN Features f ON ixf.FeatureID = f.FeatureID" + br +
            "  JOIN FeatureTypes ft ON f.FeatureTypeID = ft.FeatureTypeID" + br +
            " WHERE i.ItemTypeID = (SELECT ItemTypeID FROM ItemTypes WHERE Name = 'Recreation Site')" + br +
            "   AND f.Active = 1" + br +
            "   AND f.Hidden = 0" + br +
            " ORDER BY i.ItemID, ixf.DisplayOrder";


        // -- Site Photos --------------------

        // SQL query for list of site gallery images (by siteId), excluding the first
        private static string queryPhotosById =
            "SELECT g.ImageID AS imageId, g.IconURL AS thumbUrl, g.FullURL AS fullImageUrl, g.Caption AS caption, g.Source AS source" + br +
            "  FROM ItemsXImages ixi" + br +
            "  LEFT OUTER JOIN Images g ON ixi.ImageID = g.ImageID" + br +
            " WHERE ixi.ItemID = @0" + br +
            "   AND g.ImageTypeID = (SELECT ImageTypeID FROM ImageTypes WHERE Name = 'Gallery Image')" + br +
            "   AND g.Active = 1" + br +
            "   AND NOT EXISTS (SELECT * FROM ItemsXFirstGalleryImage WHERE ixi.ID = ID)" + br +
            " ORDER BY ixi.DisplayOrder";


        // SQL query for list of site gallery images (for ALL sites), excluding the first for each
        private static string querySitePhotos =
            "SELECT i.ItemID AS siteId, g.ImageID AS imageId, g.IconURL AS thumbUrl, g.FullURL AS fullImageUrl, g.Caption AS caption, g.Source AS source" + br +
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXImages ixi ON ixi.ItemID = i.ItemID" + br +
            "  JOIN Images g ON ixi.ImageID = g.ImageID" + br +
            " WHERE i.ItemTypeID = (SELECT ItemTypeID FROM ItemTypes WHERE Name = 'Recreation Site')" + br +
            "   AND g.ImageTypeID = (SELECT ImageTypeID FROM ImageTypes WHERE Name = 'Gallery Image')" + br +
            "   AND g.Active = 1" + br +
            "   AND NOT EXISTS (SELECT * FROM ItemsXFirstGalleryImage WHERE ixi.ID = ID)" + br + 
            " ORDER BY i.ItemID, ixi.DisplayOrder";


        // -- Site Cost Periods --------------------

        // SQL query for list of site cost periods (by siteId)
        // Deprecated (Old Availability model)
        /*
        private static string queryCostPeriodsById =
            "SELECT a.AvailStartMonth AS StartMonth, a.AvailStartDay AS StartDay, a.AvailEndMonth AS EndMonth," + br +
            "       a.AvailEndDay AS EndDay, a.MinDurationDays AS MinimumDuration, ixa.WeekdayRate, ixa.WeekendRate," + br +
            "       0 AS NotAvailable" + br +
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXAvailability ixa ON i.ItemID = ixa.ItemID" + br +
            "  JOIN Availability a ON ixa.AvailID = a.AvailID" + br +
            " WHERE i.ItemID = @0";
        */

        // SQL query for list of site cost periods (for ALL sites)
        // Deprecated (Old Availability model)
        /*
        private static string querySiteCostPeriods =
            "SELECT i.ItemID AS siteId, a.AvailStartMonth AS startMonth, a.AvailStartDay AS startDay," + br +
            "       a.AvailEndMonth AS endMonth, a.AvailEndDay AS endDay," + br +
            "       a.MinDurationDays AS minimumDuration, ixa.WeekdayRate, ixa.WeekendRate," + br +
            "       (1 - a.Available) AS notAvailable" + br +
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXAvailability ixa ON i.ItemID = ixa.ItemID" + br +
            "  JOIN Availability a ON ixa.AvailID = a.AvailID" + br +
            " WHERE i.ItemTypeID = (SELECT ItemTypeID FROM ItemTypes WHERE Name = 'Recreation Site')" + br +
            " ORDER BY i.ItemID, ixa.DisplayOrder";
        */

        // SQL query for list of site cost periods (by siteId)
        private static string queryCostPeriodsById =
            "SELECT a.AvailStartMonth AS startMonth, a.AvailStartDay AS startDay," + br +
            "       a.AvailEndMonth AS endMonth, a.AvailEndDay AS endDay, a.MinDurationDays AS minimumDuration," + br +
            "       (r.DailyFee + r.WeekdayFee) AS weekdayRate, (r.DailyFee + r.WeekendFee) AS weekendRate," + br +
            "       r.DepositDailyFee AS dailyDeposit," + br +
            "       (1 - a.Available) AS notAvailable" + br +
            "  FROM ItemsXAvailRates ixar" + br +
            "  JOIN Availability a ON ixar.AvailID = a.AvailID" + br +
            "  JOIN Rates r ON ixar.RateID = r.RateID" + br +
            " WHERE ixar.ItemID = @0" + br +
            "   AND a.Active = 1" + br +
            "   AND r.Active = 1" + br +
            " ORDER BY ixar.DisplayOrder";

        // SQL query for list of site cost periods (for ALL sites)
        private static string querySiteCostPeriods =
            "SELECT i.ItemID AS siteId, a.AvailStartMonth AS startMonth, a.AvailStartDay AS startDay," + br +
            "       a.AvailEndMonth AS endMonth, a.AvailEndDay AS endDay, a.MinDurationDays AS minimumDuration," + br +
            "       (r.DailyFee + r.WeekdayFee) AS weekdayRate, (r.DailyFee + r.WeekendFee) AS weekendRate," + br +
            "       r.DepositDailyFee AS dailyDeposit," + br +
            "       (1 - a.Available) AS notAvailable" + br +
            "  FROM Items i" + br +
            "  LEFT OUTER JOIN ItemsXAvailRates ixar ON i.ItemID = ixar.ItemID" + br +
            "  JOIN Availability a ON ixar.AvailID = a.AvailID" + br +
            "  JOIN Rates r ON ixar.RateID = r.RateID" + br +
            " WHERE i.ItemTypeID = (SELECT ItemTypeID FROM ItemTypes WHERE Name = 'Recreation Site')" + br +
            "   AND a.Active = 1" + br +
            "   AND r.Active = 1" + br +
            " ORDER BY i.ItemID, ixar.DisplayOrder";


        // return reservation costs for a site for a range of dates
        private static string querySiteCosts_pre =
            "SELECT s.ItemID, MAX(s.Name) AS SiteName, COUNT(d.[Date]) AS Nights," + br +
            "       SUM(1 * d.IsResvWeekend) AS WeekendNights," + br +
            "       MAX(r.BaseFee) + SUM(r.DailyFee) + SUM(r.WeekdayFee * (1 - d.IsResvWeekend)) +" + br +
            "       SUM(r.WeekendFee * d.IsResvWeekend) AS Total," + br +
            "       MAX(r.DepositBaseFee) + SUM(r.DepositDailyFee) AS Deposit," + br +
            "       MAX(r.ProcessorBaseFee) + SUM(r.ProcessorDailyFee) AS ProcessorFee" + br +
            "  FROM DiscreteAvailabilityRanges a" + br +
            "  JOIN ItemsXAvailRates ixr ON ixr.AvailID = a.AvailID" + br +
            "  JOIN Items s ON ixr.ItemID = s.ItemID" + br +
            "  JOIN Rates r ON ixr.RateID = r.RateID" + br +
            "  JOIN Dates d ON d.[Date] >= a.AvailStartDate AND d.[Date] <= a.AvailEndDate" + br +
            " WHERE s.ItemID = @0" + br;

        private static string querySiteCosts_post =
            " GROUP BY s.ItemID";


        // return reservation costs for a site for a range of dates
        private static string execSiteValidation_pre =
            "EXECUTE dbo.ReserveSite2" + br +
            "  @@SiteID = @0,";

        private static string execSiteValidation_post =
            "  @@UniqueID = 123123123," + br +  // dummy value
            "  @@ValidateOnly = 1";


        // return list of reserved data ranges for site (> today)
        // also includes days as "unavailable" if not on an active availability schedule
        private static string queryReservedRanges =
            // first, find those date ranges that are not included on an active availability schedule
            // we'll return these dates as unavailable (in addition to those that are reserved)
            "SELECT * FROM (" + br +
            "SELECT NULL AS ResourceID, ixr.ItemID, 'UNAVAILABLE' AS ResourceName, 'Date range not on an availability schedule' AS ResourceDescription," + br +
            "       DATEADD(DAY, 1, a.AvailEndDate) AS StartDate," + br +
            "       DATEADD(DAY, -1, b.AvailStartDate) AS EndDate," + br +
            "       NULL AS ReservationID, NULL AS UniqueID, NULL AS IsReserved, NULL AS Cancelled, NULL AS CartRefreshDateTime," + br +
            "       NULL AS Now, NULL AS Temp, NULL AS ExpiresMins" + br +
            "  FROM dbo.ItemsXAvailRates ixr" + br +
            "  JOIN dbo.DiscreteAvailabilityRanges a ON ixr.AvailID = a.AvailID" + br +
            " CROSS APPLY (" + br +
            "             SELECT TOP (1) t.AvailStartDate" + br +
            "               FROM dbo.ItemsXAvailRates ti" + br +
            "               JOIN dbo.DiscreteAvailabilityRanges t ON ti.AvailID = t.AvailID" + br +
            "              WHERE ti.ItemID = @0" + br +        // @0 = SiteID
            "                AND t.AvailStartDate > a.AvailEndDate" + br +
            "              ORDER BY t.AvailStartDate) b" + br +
            " WHERE ixr.ItemID = @0" + br +                    // @0 = SiteID
            "   AND DATEDIFF(DAY, a.AvailEndDate, b.AvailStartDate) > 1" + br +
            "   AND b.AvailStartDate >= GETDATE()" + br +      // only consider those that end in the future
            " UNION ALL" + br +

            // now find those date ranges that have already been reserved for the site in question
            "SELECT rr.ResourceID, rr.ItemID, rr.ResourceName, rr.ResourceDescription, rr.StartDateTime AS StartDate, rr.EndDateTime AS EndDate, r.ReservationID, r.UniqueID, r.IsReserved, r.Cancelled," + br +
            "       r.CartRefreshDateTime, GETDATE() AS Now, " + br +
            "       1 - r.IsReserved AS Temp, CASE IsReserved WHEN 0 THEN DATEDIFF(MINUTE, GETDATE(), DATEADD(SECOND, rr.CartTimeoutSeconds, r.CartRefreshDateTime)) WHEN 1 THEN NULL END AS ExpiresInMins" + br +
            "  FROM dbo.Reservations r" + br +
            "  JOIN dbo.ReservationResources rr ON rr.ResourceID = r.ResourceID" + br +
            " WHERE rr.ItemID = @0" + br +               // SiteID
            "   AND r.Cancelled = 0" + br +              // disregard those marked cancelled
            "   AND rr.EndDateTime > GETDATE()" + br +   // only consider those that end in the future
            "   AND (r.IsReserved = 1 OR (r.CartRefreshDateTime > DATEADD(SECOND, -1 * rr.CartTimeoutSeconds, GETDATE())))" + br +  // only those fully reserved or those temporarily reserved and within the timerout period
            ") u ORDER BY StartDate";  // sort the entire resultset by date


        // map the SiteBasic properties to columns and default criteria conditions to be used in filtered queries
        public static Dictionary<string, SqlCriteria> mapSiteBasicProps = new Dictionary<string, SqlCriteria>()
        { 
          // property                                     column             data type            default operator
            {"siteid",                    new SqlCriteria("i.ItemID",        CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"locationid",                new SqlCriteria("l.ItemID",        CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"type",                      new SqlCriteria("f0.Value",        CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"siteidentifier",            new SqlCriteria("i.Name",          CriteriaType.TEXT,   SqlOperator.EQUAL)},
            {"x",                         new SqlCriteria("ixm.CoordinateX", CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"y",                         new SqlCriteria("ixm.CoordinateY", CriteriaType.NUMBER, SqlOperator.EQUAL)},
        };

        // map SiteDetail properties to columns and default criteria conditions to be used in filtered queries
        public static Dictionary<string, SqlCriteria> mapSiteDetailProps = new Dictionary<string, SqlCriteria>()
        { 
          // property                                     column                          data type            default operator
            {"description",               new SqlCriteria("i.Description",                CriteriaType.TEXT,   SqlOperator.CONTAINS)},  // find descriptions containing VALUE
            {"maxaccommodatingunits",     new SqlCriteria("av.MaxAccommodatingUnits",     CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"minduration",               new SqlCriteria("av.MinDuration",               CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"maxduration",               new SqlCriteria("av.MaxDuration",               CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"advancedreservationperiod", new SqlCriteria("av.AdvancedReservationPeriod", CriteriaType.NUMBER, SqlOperator.EQUAL)},
            {"reservable",                new SqlCriteria("f1.Value",                     CriteriaType.NUMBER, SqlOperator.EQUAL)}
        };

        // map Reservation properties to columns and default criteria conditions to be used in filtered queries
        public static Dictionary<string, SqlCriteria> mapSiteReservationProps = new Dictionary<string, SqlCriteria>()
        { 
          // property                                     column              data type          default operator
            {"availablestartdate",        new SqlCriteria("rr.StartDateTime", CriteriaType.DATE, SqlOperator.GREATER)},  // find start dates greater than VALUE
            {"availableenddate",          new SqlCriteria("rr.EndDateTime",   CriteriaType.DATE, SqlOperator.LESSEQUAL)} // find end dates less than or equal to value
        };

        // map properties to columns and default criteria conditions to be used in filtered queries
        public static Dictionary<string, SqlCriteria> mapSiteCostParams = new Dictionary<string, SqlCriteria>()
        { 
          // property                      column     data type          default operator
            {"startdate", new SqlCriteria("d.[Date]", CriteriaType.DATE, SqlOperator.GREATEREQUAL)},  // find dates greater than or equal to the startdate VALUE
            {"enddate",   new SqlCriteria("d.[Date]", CriteriaType.DATE, SqlOperator.LESS)}           // find dates less than the enddate VALUE
        };

        // map properties to columns and default criteria conditions to be used in filtered queries
        public static Dictionary<string, SqlCriteria> mapSiteValidateParams = new Dictionary<string, SqlCriteria>()
        { 
          // property                      column       data type          default operator
            {"startdate", new SqlCriteria("@StartDate", CriteriaType.DATE, SqlOperator.EQUAL)},
            {"enddate",   new SqlCriteria("@EndDate",   CriteriaType.DATE, SqlOperator.EQUAL)}
        };

        public SiteRepository()
        {
            db = new Database("Kinsail");
            setup();
        }

        public SiteRepository(IDatabase database)
        {
            db = database;
            setup();
        }

        // retrieve some important key values, so we don't have to keep querying for them each time
        private void setup()
        {
            siteItemTypeId = db.ExecuteScalar<long>("SELECT ItemTypeID from ItemTypes WHERE Name = 'Recreation Site'");
            locationItemTypeId = db.ExecuteScalar<long>("SELECT ItemTypeID from ItemTypes WHERE Name = 'Recreation Location'");
            galleryImageTypeId = db.ExecuteScalar<long>("SELECT ImageTypeID from ImageTypes WHERE Name = 'Gallery Image'");
            siteTypeFeatureId = db.ExecuteScalar<long>("SELECT FeatureID FROM Features WHERE Name = 'Site Type'");
            reservableFeatureId = db.ExecuteScalar<long>("SELECT FeatureID FROM Features WHERE Name = 'Reservable'");

            // load the complete list of all features that are defined in the database
            List<object[]> features = db.Fetch<object[]>(queryAllFeatures);
            foreach (object[] row in features)
            {
                CriteriaType t;
                switch ((string)row[2])
                {
                    case "NUMBER":
                    case "ENUM":
                        t = CriteriaType.NUMBER;
                        break;
                    case "DATE":
                        t = CriteriaType.DATE;
                        break;
                    case "TEXT":
                    default:
                        t = CriteriaType.TEXT;
                        break;
                }
                allFeatures.Add((string)row[0], new SqlCriteria((string)row[0], t, SqlOperator.NONE, (long)row[1]));
            }
        }


        // return list of SiteBasic objects
        public List<SiteBasic> GetAll(Dictionary<string, string> queryParams = null)
        {
            var sql = NPoco.Sql.Builder
                .Append(selectSiteBasic)
                .Append(fromJoinSiteBasic, siteTypeFeatureId, galleryImageTypeId)
                .Append(whereSites, siteItemTypeId, locationItemTypeId);

            // any URI filter parameters to add to the query?
            if (queryParams != null)
            {
                sql = sql.Append(generateFilterClauses(queryParams, false, true));
            }

            sql = sql.Append(orderSites);
            //Debug.Print(sql.SQL);

            List<SiteBasic> sites = db.Fetch<SiteBasic, MapCoordinates, GalleryImage>(sql);
            return sites;
        }

        // return list of SiteBasic objects for a Location
        public List<SiteBasic> GetAllForLocation(long locationId, Dictionary<string, string> queryParams = null)
        {
            var sql = NPoco.Sql.Builder
                .Append(selectSiteBasic)
                .Append(fromJoinSiteBasic, siteTypeFeatureId, galleryImageTypeId)
                .Append(whereSitesForLocation, siteItemTypeId, locationItemTypeId, locationId);

            // any URI filter parameters to add to the query?
            if (queryParams != null)
            {
                sql = sql.Append(generateFilterClauses(queryParams, false, true));
            }

            sql = sql.Append(orderSites);
            //Debug.Print(sql.SQL);

            List<SiteBasic> sites = db.Fetch<SiteBasic, MapCoordinates, GalleryImage>(sql);
            return sites;
        }

        public List<SiteDetail> GetAllDetails(Dictionary<string, string> queryParams = null)
        {

            // FIX: .Append(whereSiteById, siteItemTypeId, locationItemTypeId, siteId)
            
            // get sites
            var sql = NPoco.Sql.Builder
                .Append(selectSiteDetail)
                .Append(fromJoinSiteDetail, siteTypeFeatureId, reservableFeatureId)
                .Append(whereSites, siteItemTypeId, locationItemTypeId);

            // any URI filter parameters to add to the query?
            if (queryParams != null)
            {
                sql = sql.Append(generateFilterClauses(queryParams, true, true));
            }

            sql = sql.Append(orderSites);
            //Debug.Print(sql.SQL);

            List<SiteDetail> sites = db.Fetch<SiteDetail, MapCoordinates, GalleryImage>(sql);

            /* SLOW METHOD - issuing child queries for each row in the result set to retrieve children
            foreach (SiteDetail site in sites)
            {
                // get features for each site
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<FeatureAttribute<object>> features = db.Fetch<FeatureAttribute<object>>(selectFeatures, siteTypeFeatureId, site.siteId);
                site.features = features.ToArray();

                // get cost periods for each site
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<CostPeriod> costPeriods = db.Fetch<CostPeriod>(selectCostPeriods, site.siteId);
                site.cost = new CostStructure(costPeriods.ToArray());

                // get gallery images for each location, excluding the first one
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<GalleryImage> photos = db.Fetch<GalleryImage>(selectPhotos, site.siteId);
                site.photos = photos.ToArray();
            }
            return sites;
            */

            // FASTER METHOD - retrieve children in separate Lists, then loop through and connect them to the parent
            // (fewer SQL queries)
            var features2 = db.FetchOneToMany<SiteDTO, FeatureAttribute<object>>(x => x.siteId, x => x.id, querySiteFeatures);
            var costPeriods2 = db.FetchOneToMany<SiteDTO, CostPeriod>(x => x.siteId, querySiteCostPeriods);
            var photos2 = db.FetchOneToMany<SiteDTO, GalleryImage>(x => x.siteId, x => x.imageId, querySitePhotos);
            foreach (SiteDetail site in sites)
            {
                var f = features2.Find(x => x.siteId == site.siteId);
                if (f != null)
                {
                    site.features = f.features.ToArray();
                }

                var cp = costPeriods2.Find(x => x.siteId == site.siteId);
                if (cp != null)
                {
                    site.cost = new CostStructure(cp.costs.ToArray());
                }

                var p = photos2.Find(x => x.siteId == site.siteId);
                if (p != null)
                {
                    site.photos = p.photos.ToArray();
                }
            }
            return sites;

        }

        public List<SiteDetail> GetAllDetailsForLocation(long locationId, Dictionary<string, string> queryParams = null)
        {
            // get sites
            var sql = NPoco.Sql.Builder
                .Append(selectSiteDetail)
                .Append(fromJoinSiteDetail, siteTypeFeatureId, reservableFeatureId)
                .Append(whereSitesForLocation, siteItemTypeId, locationItemTypeId, locationId);

            // any URI filter parameters to add to the query?
            if (queryParams != null)
            {
                sql = sql.Append(generateFilterClauses(queryParams, true, true));
            }

            sql = sql.Append(orderSites);
            //Debug.Print(sql.SQL);

            List<SiteDetail> sites = db.Fetch<SiteDetail, MapCoordinates, GalleryImage>(sql);

            // SLOW METHOD - issuing child queries for each row in the result set to retrieve children
            /*
            foreach (SiteDetail site in sites)
            {
                // get features for each site
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<FeatureAttribute<object>> features = db.Fetch<FeatureAttribute<object>>(selectFeatures, siteTypeFeatureId, site.siteId);
                site.features = features.ToArray();

                // get cost periods for each site
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<CostPeriod> costPeriods = db.Fetch<CostPeriod>(selectCostPeriods, site.siteId);
                site.cost = new CostStructure(costPeriods.ToArray());

                // get gallery images for each location, excluding the first one
                // can't automatically include this in the primary query since NPoco can't do both 
                // nested and one-to-many properties in a single automatic mapping
                List<GalleryImage> photos = db.Fetch<GalleryImage>(selectPhotos, site.siteId);
                site.photos = photos.ToArray();
            }
            */

            // FASTER METHOD - retrieve children in separate Lists, then loop through and connect them to the parent
            // (fewer SQL queries)
            var features2 = db.FetchOneToMany<SiteDTO, FeatureAttribute<object>>(x => x.siteId, x => x.id, querySiteFeatures);
            var costPeriods2 = db.FetchOneToMany<SiteDTO, CostPeriod>(x => x.siteId, querySiteCostPeriods);
            var photos2 = db.FetchOneToMany<SiteDTO, GalleryImage>(x => x.siteId, x => x.imageId, querySitePhotos);
            foreach (SiteDetail site in sites)
            {
                var f = features2.Find(x => x.siteId == site.siteId);
                if (f != null)
                {
                    site.features = f.features.ToArray();
                }

                var cp = costPeriods2.Find(x => x.siteId == site.siteId);
                if (cp != null)
                {
                    site.cost = new CostStructure(cp.costs.ToArray());
                }

                var p = photos2.Find(x => x.siteId == site.siteId);
                if (p != null)
                {
                    site.photos = p.photos.ToArray();
                }
            }
            return sites;
        }

        public SiteBasic GetbyId(long siteId)
        {
            var sql = NPoco.Sql.Builder
                .Append(selectSiteBasic)
                .Append(fromJoinSiteBasic, siteTypeFeatureId, galleryImageTypeId)
                .Append(whereSiteById, siteItemTypeId, locationItemTypeId, siteId)
                .Append(orderSites);

            //Debug.Print(sql.SQL);

            List<SiteBasic> sites = db.Fetch<SiteBasic, MapCoordinates, GalleryImage>(sql);
            return sites.ElementAtOrDefault(0);
        }

        public SiteDetail GetDetailbyId(long siteId)
        {
            var sql = NPoco.Sql.Builder
                .Append(selectSiteDetail)
                .Append(fromJoinSiteDetail, siteTypeFeatureId, reservableFeatureId)
                .Append(whereSiteById, siteItemTypeId, locationItemTypeId, siteId)
                .Append(orderSites);

            //Debug.Print(sql.SQL);

            List<SiteDetail> sites = db.Fetch<SiteDetail, MapCoordinates, GalleryImage>(sql);
            SiteDetail site = sites.ElementAtOrDefault(0);

            // get features for site
            // can't automatically include this in the primary query since NPoco can't do both 
            // nested and one-to-many properties in a single automatic mapping
            List<FeatureAttribute<object>> features = db.Fetch<FeatureAttribute<object>>(queryFeaturesById, siteTypeFeatureId, siteId);
            site.features = features.ToArray();

            // get cost periods for site
            // can't automatically include this in the primary query since NPoco can't do both 
            // nested and one-to-many properties in a single automatic mapping
            List<CostPeriod> costPeriods = db.Fetch<CostPeriod>(queryCostPeriodsById, siteId);
            site.cost = new CostStructure(costPeriods.ToArray());

            // get gallery images for each location, excluding the first one
            // can't automatically include this in the primary query since NPoco can't do both 
            // nested and one-to-many properties in a single automatic mapping
            List<GalleryImage> photos = db.Fetch<GalleryImage>(queryPhotosById, site.siteId);
            site.photos = photos.ToArray();

            return site;
        }

        public SiteAvailability GetAvailabilitybyId(long siteId)
        {
            var sql = NPoco.Sql.Builder
                .Append(selectSiteBasic)
                .Append(fromJoinSiteBasic, siteTypeFeatureId, galleryImageTypeId)
                .Append(whereSiteById, siteItemTypeId, locationItemTypeId, siteId)
                .Append(orderSites);

            //Debug.Print(sql.SQL);

            List<SiteAvailability> sites = db.Fetch<SiteAvailability, MapCoordinates, GalleryImage>(sql);
            SiteAvailability site = sites.ElementAtOrDefault(0);

            // Get Availability
            List<DateRange> dates = db.Fetch<DateRange>(queryReservedRanges, site.siteId);
            site.bookedRanges = dates.ToArray();

            return site;
        }
                
        public ReservationCost GetCostbyId(long siteId, Dictionary<string, string> queryParams = null)
        {
            Dictionary<string, SqlCriteria> filterParams = new Dictionary<string, SqlCriteria>();
            StringBuilder s = new StringBuilder();

            string columnPart;
            string operatorPart;

            // parse the URI query params
            foreach (var item in queryParams)
            {
                columnPart = SqlCriteria.getColumnPart(item.Key);
                operatorPart = SqlCriteria.getOperatorPart(item.Key);
                SqlOperator op;

                // lookup params
                if (mapSiteCostParams.ContainsKey(columnPart))
                {
                    // copy the criteria object from the lookup map
                    filterParams.Add(columnPart, mapSiteCostParams[columnPart].clone());

                    // override the default operator, if the user has specified one (not recommended here)
                    op = SqlCriteria.getOperator(operatorPart);
                    if (op != SqlOperator.NONE)
                    {
                        filterParams[columnPart].oper = op;
                    }

                    // inject the user-supplied data value(s) into the copied criteria
                    filterParams[columnPart].value = item.Value;

                    //Debug.Print("Find site costs WHERE: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                }
                else
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);  // invalid query parameter
                }
            }

            // user must supply both a start and end date as parameters
            if (filterParams.Count != 2) {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }


            // get costs
            var sql = NPoco.Sql.Builder
                .Append(querySiteCosts_pre, siteId);

            // append filter conditions for the start and end date
            foreach (var criteria in filterParams)
            {
                sql = sql.Append("   AND " + criteria.Value.getSql());
            }
            sql = sql.Append(querySiteCosts_post);

            // retrieve cost info
            ReservationCost cost = db.First<ReservationCost>(sql);

            //return new Cost(10.0F, 5.0F);
            return cost;
        }


        public SPResult GetValidationbyId(long siteId, Dictionary<string, string> queryParams = null)
        {
            Dictionary<string, SqlCriteria> filterParams = new Dictionary<string, SqlCriteria>();
            StringBuilder s = new StringBuilder();

            string columnPart;
            string operatorPart;

            // parse the URI query params
            foreach (var item in queryParams)
            {
                columnPart = SqlCriteria.getColumnPart(item.Key);
                operatorPart = SqlCriteria.getOperatorPart(item.Key);
                SqlOperator op;

                // lookup params
                if (mapSiteValidateParams.ContainsKey(columnPart))
                {
                    // copy the criteria object from the lookup map
                    filterParams.Add(columnPart, mapSiteValidateParams[columnPart].clone());

                    // override the default operator, if the user has specified one (not recommended here)
                    op = SqlCriteria.getOperator(operatorPart);
                    if (op != SqlOperator.NONE)
                    {
                        filterParams[columnPart].oper = op;
                    }

                    // inject the user-supplied data value(s) into the copied criteria
                    filterParams[columnPart].value = item.Value;

                    //Debug.Print("Find site costs WHERE: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                }
                else
                {
                    throw new HttpResponseException(HttpStatusCode.BadRequest);  // invalid query parameter
                }
            }

            // user must supply both a start and end date as parameters
            if (filterParams.Count != 2)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest);
            }


            // validate reservation request
            var sql = NPoco.Sql.Builder
                .Append(execSiteValidation_pre, siteId);

            // append parameters for the start and end date
            foreach (var criteria in filterParams)
            {
                sql = sql.Append("  @" + criteria.Value.getSql() + ", ");  //add prepended @ to escape column names for NPoco
            }
            sql = sql.Append(execSiteValidation_post);
            //Debug.Print("SQL=" + sql.SQL);
            

            // retrieve validation result
            SPResult result = db.First<SPResult>(sql);

            //return 0
            return result;
        }
        
        // generate additional WHERE clauses based on the passed in URI querystring parameters
        // handles:
        // - unknown params (skip them in the SQL, for now)
        // - params for object properties (including a default operator that makes sense)
        // - params for feature children, including multiples
        // - params for mixed sets of object properties and feature children
        // - params with mixed case
        // - empty params (e.g., restrooms=)
        // - quoting for string-valued params (prevents sql injection)
        // - parsing for all criteria values (prevents sql injection for types other than strings)
        // - support for operators, including mixed case
        private string generateFilterClauses(Dictionary<string, string> queryParams, bool detailsFlag, bool featuresFlag)
        {
            Dictionary<string, SqlCriteria> filterProperties = new Dictionary<string, SqlCriteria>();
            Dictionary<string, SqlCriteria> filterFeatures = new Dictionary<string, SqlCriteria>();
            Dictionary<string, SqlCriteria> filterReserved = new Dictionary<string, SqlCriteria>();
            Dictionary<string, SqlCriteria> filterOther = new Dictionary<string, SqlCriteria>();
            StringBuilder s = new StringBuilder();

            string columnPart;
            string operatorPart;

            // parse the query params into separate groups (properties, features, other)
            foreach (var item in queryParams)
            {
                columnPart = SqlCriteria.getColumnPart(item.Key);
                operatorPart = SqlCriteria.getOperatorPart(item.Key);
                SqlOperator op;

                if (mapSiteBasicProps.ContainsKey(columnPart) |                  // is the parameter name a property of SiteBasic object?
                    (detailsFlag & mapSiteDetailProps.ContainsKey(columnPart)))  // is the parameter name a property of SiteDetail object?
                {
                    // copy the criteria object from the lookup map
                    if (mapSiteBasicProps.ContainsKey(columnPart))
                    {
                        filterProperties.Add(columnPart, mapSiteBasicProps[columnPart].clone());
                    }
                    else
                    {
                        filterProperties.Add(columnPart, mapSiteDetailProps[columnPart].clone());
                    }

                    // override the default operator, if the user has specified one
                    op = SqlCriteria.getOperator(operatorPart);
                    if (op != SqlOperator.NONE)
                    {
                        filterProperties[columnPart].oper = op;
                    }

                    // inject the user-supplied data value(s) into the copied criteria
                    filterProperties[columnPart].value = item.Value;

                    //Debug.Print("Find sites WHERE: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                }
                else
                {
                    if (featuresFlag & allFeatures.ContainsKey(item.Key))  // is the parameter name a Feature associated with the LocationDetail object?
                    {
                        // copy the criteria object from the feature lookup map
                        filterFeatures.Add(columnPart, allFeatures[columnPart].clone());

                        // override the default operator, if the user has specified one
                        op = SqlCriteria.getOperator(operatorPart);
                        if (op != SqlOperator.NONE)
                        {
                            filterFeatures[columnPart].oper = op;
                        }

                        // inject the user-supplied data value(s) into the copied criteria
                        filterFeatures[columnPart].value = item.Value;

                        //Debug.Print("Find sites WHERE feature [FeatureID=" + allFeatures[item.Key].id + "]: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                    }
                    else 
                    {
                        if (mapSiteReservationProps.ContainsKey(columnPart))  // is the parameter name a property of a Reservation object?
                        {
                            filterReserved.Add(columnPart, mapSiteReservationProps[columnPart].clone());

                            // override the default operator, if the user has specified one
                            op = SqlCriteria.getOperator(operatorPart);
                            if (op != SqlOperator.NONE)
                            {
                                filterReserved[columnPart].oper = op;
                            }

                            // inject the user-supplied data value(s) into the copied criteria
                            filterReserved[columnPart].value = item.Value;

                            //Debug.Print("Find sites WHERE not reserved: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                        }
                        else // parameter does not match a property or feature
                        {
                            op = SqlCriteria.getOperator(operatorPart);
                            filterOther.Add(columnPart, new SqlCriteria(columnPart, CriteriaType.TEXT, SqlCriteria.getOperator(operatorPart), item.Value));

                            //Debug.Print("Ignoring unknown parameter: " + columnPart + " " + Enum.GetName(op.GetType(), op) + " " + item.Value);
                        }
                    }
                }
            }

            // generate WHERE clauses for properties
            foreach (var criteria in filterProperties)
            {
                s.AppendFormat("   AND " + criteria.Value.getSql() + br);
            }

            // generate WHERE clauses for features
            if (filterFeatures.Count > 0)
            {
                s.AppendFormat(andWhereSiteHasFeatures_pre, siteItemTypeId);
                foreach (var criteria in filterFeatures)
                {
                    s.AppendFormat(andWhereSiteFeature, allFeatures[criteria.Key].id, criteria.Value.getSql(false));
                    if (filterFeatures.Last().Key != criteria.Key)
                    {
                        s.Append(" OR ");
                    }
                }
                s.AppendFormat(andWhereSiteHasFeatures_post, filterFeatures.Count);
            }

            // generate WHERE clauses for reservation properties
            if (filterReserved.Count > 0)
            {
                s.AppendFormat(andWhereSiteReserved_pre, siteItemTypeId);
                foreach (var criteria in filterReserved)
                {
                    s.AppendFormat("                           AND " + criteria.Value.getSql() + br);
                }
                s.AppendFormat(andWhereSiteReserved_post);
            }

            //Debug.Print(s.ToString());
            return s.ToString();
        }

    }
}