using DingTalk.Api;
using DingTalk.Api.Request;
using DingTalk.Api.Response;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Mvc;
using X.PagedList;

namespace GetDataFromDingDemo.Controllers
{
    public class ProductController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        private static readonly string agentId = ConfigurationManager.AppSettings["agentId"];// 从配置文件中获取应用Id

        #region 通用方法
        /// <summary>
        /// 时间戳转换
        /// </summary>
        /// <param name="timeStamp"></param>
        /// <returns></returns>
        private DateTime StampToDateTime(string timeStamp)
        {
            DateTime dateTimeStart = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1));
            long lTime = long.Parse(timeStamp + "0000");
            TimeSpan toNow = new TimeSpan(lTime);

            return dateTimeStart.Add(toNow);
        }
        #endregion

        #region 从配置文件中获取配置相关
        public string getCorpId()
        {
            return ConfigurationManager.AppSettings["corpId"].ToString();
        }
        #endregion

        #region 通过免登授权码获取用户UserId
        public string getUserByCode(string code)
        {
            IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/v2/user/getuserinfo");
            OapiV2UserGetuserinfoRequest req = new OapiV2UserGetuserinfoRequest();
            req.Code = code;
            OapiV2UserGetuserinfoResponse rsp = client.Execute(req, AccessToken.AccessToken.GetAccessToken());
            return JsonConvert.SerializeObject(rsp.Result);
        }
        #endregion

        #region 获取打卡结果
        public class CheckData
        {
            public string timeResult { get; set; }
            public string checkType { get; set; }
            public string userCheckTime { get; set; }
        }

        public string getCheck(string userId)
        {
            DateTime date = Convert.ToDateTime("2021-12-14");
            var checkList = new List<OapiAttendanceListResponse.RecordresultDomain>();
            var offSet = 0;
            var limit = 50;
            var hasNext = true;
            while (hasNext)
            {
                IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/attendance/list");
                OapiAttendanceListRequest request = new OapiAttendanceListRequest
                {
                    WorkDateFrom = date.ToString("yyyy-MM-dd 00:00:00"),//查询考勤打卡记录的起始工作日
                    WorkDateTo = date.ToString("yyyy-MM-dd 00:00:00"),//查询考勤打卡记录的结束工作日
                    UserIdList = new List<string>() { userId },//查询的员工的userId
                    Offset = offSet,//获取考勤数据的起始点,相当于页数的索引
                    Limit = limit//表示获取考勤数据的条数，最大不能超过50条
                };
                OapiAttendanceListResponse response = client.Execute(request, AccessToken.AccessToken.GetAccessToken());
                if (response.Errcode == 0)
                {
                    if (response.Recordresult != null && response.Recordresult.Count > 0)
                    {
                        //将获取的打卡结果集合组成一个集合
                        checkList = checkList.Concat(response.Recordresult).ToList();
                    }
                }

                offSet++;
                hasNext = response.HasMore;
            }

            var data = new List<CheckData>();
            data = checkList.Select(s => new CheckData()
            {
                checkType = (s.CheckType == "OnDuty" ? "上班" : "下班"),
                timeResult = (s.TimeResult == "Normal" ? "正常" : s.TimeResult == "Early" ? "早退" : s.TimeResult == "Late" ? "迟到" : s.TimeResult == "SeriousLate" ? "严重迟到" : s.TimeResult == "Absenteeism" ? "旷工迟到" : s.TimeResult == "NotSigned" ? "未打卡" : ""),
                userCheckTime = StampToDateTime(s.UserCheckTime).ToString("yyyy-MM-dd HH:mm")
            }).ToList();

            return JsonConvert.SerializeObject(data);
        }
        #endregion

        #region 获取报表列值
        public class StatisticscData
        {
            public string type { get; set; }
            public List<vals> dataVals { get; set; }

            public class vals
            {
                public string date { get; set; }
                public string value { get; set; }
            }
        }

        public string getStatisticsc(string userId)
        {
            //获取报表列定义          
            OapiAttendanceGetattcolumnsResponse response = GetAttcolumns(AccessToken.AccessToken.GetAccessToken());
            //取获报表列定义集合的前10个集合里的列的Id
            var columnIdList = response.Result.Columns.Select(s => Convert.ToString(s.Id)).Take(10).ToList();
            //将获取到的列的Id拼接成一个字符串
            string columnId = "";
            foreach (var it in columnIdList)
            {
                columnId = columnId + "," + it;
            }
            columnId = columnId.Substring(1);

            //调用钉钉接口获取报表列值
            DateTime fromDate = Convert.ToDateTime("2021-11-01");
            DateTime toDate = Convert.ToDateTime("2021-11-30");
            var result = GetColumnVal(userId, columnId, fromDate, toDate, AccessToken.AccessToken.GetAccessToken());

            var data = new List<StatisticscData>();
            data = result.Result.ColumnVals.Select(s => new StatisticscData()
            {
                type = response.Result.Columns.FirstOrDefault(f => f.Id == s.ColumnVo.Id).Name,
                dataVals = s.ColumnVals.Select(s2 => new StatisticscData.vals()
                {
                    date = s2.Date,
                    value = s2.Value
                }).ToList()
            }).ToList();

            return JsonConvert.SerializeObject(data);
        }

        /// <summary>
        /// 获取报表列定义
        /// </summary>
        /// <param name="token">Token</param>
        /// <returns></returns>
        public static OapiAttendanceGetattcolumnsResponse GetAttcolumns(string token)
        {
            IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/attendance/getattcolumns");
            OapiAttendanceGetattcolumnsRequest req = new OapiAttendanceGetattcolumnsRequest();
            OapiAttendanceGetattcolumnsResponse response = client.Execute(req, token);
            return response;
        }

        /// <summary>
        /// 获取报表列值钉钉接口
        /// </summary>
        /// <param name="userid">用户UserId</param>
        /// <param name="columnIdList">查询的列的Id,多值用英文逗号分隔，最大长度20</param>
        /// <param name="fromDate">开始时间</param>
        /// <param name="toDate">结束时间，结束时间减去开始时间必须在31天以内</param>
        /// <param name="token">Token</param>
        /// <returns></returns>
        public static OapiAttendanceGetcolumnvalResponse GetColumnVal(string userid, string columnIdList, DateTime fromDate, DateTime toDate, string token)
        {
            IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/attendance/getcolumnval");
            OapiAttendanceGetcolumnvalRequest req = new OapiAttendanceGetcolumnvalRequest();
            req.Userid = userid;
            req.ColumnIdList = columnIdList;
            req.FromDate = fromDate;
            req.ToDate = toDate;
            OapiAttendanceGetcolumnvalResponse response = client.Execute(req, token);
            return response;
        }
        #endregion

        #region 获取排班信息
        public class ScheduleData
        {
            public string check_type { get; set; }
            public string plan_check_time { get; set; }
        }

        public string getSchedule(string userId)
        {
            //查询的时间范围一个月
            var startDateVal = Convert.ToDateTime("2021-11-01");//开始日期
            var endDateVal = Convert.ToDateTime("2021-11-01").AddMonths(1).AddDays(-1);//结束日期

            //获取一个月所有天数
            var dateList = new List<DateTime>();
            while (startDateVal <= endDateVal)
            {
                dateList.Add(startDateVal);
                startDateVal = startDateVal.AddDays(1);
            }

            // 分页调接口查询人员排班信息
            var sourceData = new List<ScheduleInfoModel>();
            var pageIndex = 1;
            var pageSize = 7;
            var hasMore = true;
            while (hasMore)
            {
                var datePageList = dateList.ToPagedList(pageIndex, pageSize);//每次查7天的数据
                var startDateFor = datePageList.OrderBy(ob => ob).FirstOrDefault();//查询开始时间
                var endDateFor = datePageList.OrderByDescending(ob => ob).FirstOrDefault();//查询结束时间

                IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/attendance/schedule/listbyusers");
                OapiAttendanceScheduleListbyusersRequest req = new OapiAttendanceScheduleListbyusersRequest();
                req.OpUserId = userId;//用户的userid
                req.Userids = userId;//要查询的人员userid列表，多个userid用逗号分隔，一次最多可传入50个
                req.FromDateTime = ConvertDateTimeInt(startDateFor);//起始日期，Unix时间戳，单位毫秒
                req.ToDateTime = ConvertDateTimeInt(endDateFor);//结束日期，Unix时间戳，单位毫秒
                OapiAttendanceScheduleListbyusersResponse rsp = client.Execute(req, AccessToken.AccessToken.GetAccessToken());

                if (rsp.Errcode == 0 && rsp.Result != null)
                {
                    foreach (var result in rsp.Result.Where(w => w.ShiftId != 0))
                    {
                        //将查询到的排班信息都存放到sourceData集合中
                        var schedule = new ScheduleInfoModel()
                        {
                            ClassId = result.ShiftId.ToString(),//班次ID，该值为0，说明是休息
                            ScheduleId = result.Id.ToString(),//
                            Date = result.WorkDate,//工作日，代表具体哪一天的排班
                            CheckType = result.CheckType,//考勤类型：Onduty：上班打卡，OffDuty：下班打卡
                            IsRest = result.IsRest,//是否休息：Y：当天排休， N：当天不休息
                            PlanCheckTime = result.PlanCheckTime//计划打卡时间
                        };

                        sourceData.Add(schedule);
                    }


                }

                if (rsp.Errcode != 0)
                {
                    break;
                }

                hasMore = datePageList.HasNextPage;
                pageIndex++;
            }

            var data = new List<ScheduleData>();
            data = sourceData.Select(s => new ScheduleData()
            {
                check_type = (s.CheckType == "Onduty" ? "上班打卡" : "下班打卡"),
                plan_check_time = s.PlanCheckTime
            }).ToList();

            return JsonConvert.SerializeObject(data);
        }

        //排班信息的模型类
        public class ScheduleInfoModel
        {
            public string Date { get; set; }
            public string ClassId { get; set; }
            public string ScheduleId { get; set; }
            public string ClassName { get; set; }
            public string CheckType { get; set; }
            public string IsRest { get; set; }
            public string PlanCheckTime { get; set; }
        }


        //DateTime时间格式转换为Unix时间戳格式  
        public static long ConvertDateTimeInt(System.DateTime time)
        {
            System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1));
            return Convert.ToInt64((time - startTime).TotalMilliseconds);
        }
        #endregion

        #region 获取假期数据
        public class HolidayData
        {
            public string type { get; set; }
            public List<vals> dataVals { get; set; }

            public class vals
            {
                public string date { get; set; }
                public string value { get; set; }
            }
        }

        public string getHoliday(string userId)
        {
            //获取报表列定义
            var AttcolumnsText = GetAttcolumns(AccessToken.AccessToken.GetAccessToken());
            //报表列定义的结果集合
            var AttcolumnsText1 = AttcolumnsText.Result.Columns.ToList();

            //取出所有假期的名称
            var leaveNamesList = AttcolumnsText1.Where(w => w.Alias == "leave_").Select(s => s.Name).ToList();
            //将所有假期名称拼接成一个字符串
            string leaveNames = "";
            foreach (var ln in leaveNamesList)
            {
                leaveNames = leaveNames + "," + ln;
            }
            leaveNames = leaveNames.Substring(1);
            DateTime fromDate = Convert.ToDateTime("2021-11-01");
            DateTime toDate = Convert.ToDateTime("2021-11-30");
            var LeaveTimeText = GetLeaveTime(userId, leaveNames, fromDate, toDate, AccessToken.AccessToken.GetAccessToken());

            var data = new List<HolidayData>();
            data = LeaveTimeText.Result.Columns.Select(s => new HolidayData()
            {
                type = s.Columnvo.Name,
                dataVals = s.Columnvals.Where(w => Convert.ToDecimal(w.Value) != 0).Select(s2 => new HolidayData.vals()
                {
                    date = s2.Date,
                    value = s2.Value
                }).ToList()
            }).ToList();

            return JsonConvert.SerializeObject(data.Where(w => w.dataVals.Count > 0));
        }

        /// <summary>
        /// 获取假期数据钉钉接口
        /// </summary>
        /// <param name="userid">用户UserId</param>
        /// <param name="leaveNames">假期名称，多个用英文逗号分隔，最大长度20</param>
        /// <param name="fromDate">开始时间</param>
        /// <param name="toDate">结束时间，结束时间减去开始时间必须在31天以内</param>
        /// <param name="token">Token</param>
        /// <returns></returns>
        public static OapiAttendanceGetleavetimebynamesResponse GetLeaveTime(string userid, string leaveNames, DateTime fromDate, DateTime toDate, string token)
        {
            IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/attendance/getleavetimebynames");
            OapiAttendanceGetleavetimebynamesRequest req = new OapiAttendanceGetleavetimebynamesRequest();
            req.Userid = userid;
            req.LeaveNames = leaveNames;
            req.FromDate = fromDate;
            req.ToDate = toDate;
            OapiAttendanceGetleavetimebynamesResponse response = client.Execute(req, token);
            return response;
        }
        #endregion

        #region 通知钉钉请假审批通过
        public decimal leaveApprove(string userId)
        {
            long bizType = 3;
            // 审批单跳转地址，根据系统功能自行填写即可
            string jumpUrl = "https://aflow.dingtalk.com/dingtalk/mobile/homepage.htm?corpid=ding3465ke3245&dd_progress=false&back=native&procInstId=6b408d2b-80be-4374-be99-a310168c2c57#approval";
            string fromTime = "2021-12-14 08:00";
            string toTime = "2021-12-14 10:00";
            string durationUnit = "hour";
            long calculateModel = 1;
            string tagName = "请假";
            string subType = "年休假";
            string approveId = Guid.NewGuid().ToString();
            var result = ApprovalNotice(userId, bizType, fromTime, toTime, durationUnit, calculateModel, tagName, subType, approveId, jumpUrl, AccessToken.AccessToken.GetAccessToken());
            if (result.Errcode == 0)
            {
                return 1;
            }
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// 通知审批通过接口
        /// </summary>
        /// <param name="userid">用户UserId</param>
        /// <param name="bizType">审批单类型：1：加班 2：出差、外出 3：请假</param>
        /// <param name="fromTime">开始时间。开始时间不能早于当前时间前31天</param>
        /// <param name="toTime">结束时间。结束时间减去开始时间的天数不能超过31天。biz_type为1时结束时间减去开始时间不能超过1天</param>
        /// <param name="durationUnit">时长单位，支持格式如下：day、halfDay、hour：biz_type为1时仅支持hour</param>
        /// <param name="calculateModel">计算方法：0：按自然日计算 1：按工作日计算</param>
        /// <param name="tagName">审批单类型名称，最大长度20个字符</param>
        /// <param name="subType">子类型名称，最大长度20个字符(审批单类型biz_type=3时，该参数必传)</param>
        /// <param name="approveId">审批单ID，最大长度100个字符，自定义值</param>
        /// <param name="jumpUrl">审批单跳转地址，最大长度200个字符</param>
        /// <param name="token">Token</param>
        /// <returns></returns>
        public static OapiAttendanceApproveFinishResponse ApprovalNotice(string userid, long bizType, string fromTime, string toTime, string durationUnit, long calculateModel, string tagName, string subType, string approveId, string jumpUrl, string token)
        {
            IDingTalkClient client = new DefaultDingTalkClient("https://oapi.dingtalk.com/topapi/attendance/approve/finish");
            OapiAttendanceApproveFinishRequest req = new OapiAttendanceApproveFinishRequest();
            req.Userid = userid;
            req.BizType = bizType;
            req.FromTime = fromTime;
            req.ToTime = toTime;
            req.DurationUnit = durationUnit;
            req.CalculateModel = calculateModel;
            req.TagName = tagName;
            req.SubType = subType;
            req.ApproveId = approveId;
            req.JumpUrl = jumpUrl;
            OapiAttendanceApproveFinishResponse response = client.Execute(req, token);
            return response;
        }
        #endregion
    }
}