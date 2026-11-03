import fs from 'node:fs/promises';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const out='D:/code-demo/Quan-ly-cua-hang-hoa/outputs/data-analyst';
await fs.mkdir(out,{recursive:true});
const wb=Workbook.create();
const road=wb.worksheets.add('Lộ trình');
const res=wb.worksheets.add('Tài nguyên');
const proj=wb.worksheets.add('Dự án');
const weeks=[
['1','Tư duy phân tích','Quy trình DA; câu hỏi kinh doanh; loại dữ liệu, KPI và data dictionary.','Chọn bài toán doanh thu giảm; viết 5 câu hỏi, định nghĩa doanh thu, đơn hàng và kỳ so sánh.','Bản mô tả bài toán, dữ liệu cần có và 3 KPI có công thức.','R01, R02'],
['2','Excel nền tảng','Table, kiểu dữ liệu, sort/filter, IF, SUMIFS, COUNTIFS, XLOOKUP hoặc INDEX/MATCH.','Làm sạch bảng bán hàng: kiểu ngày, khoảng trắng, trùng lặp; đối chiếu tổng trước và sau.','Bảng sạch và ghi chú các quyết định xử lý.','R01, R02'],
['3','Excel phân tích','PivotTable, PivotChart, biểu đồ, định dạng có điều kiện; Power Query nhập và biến đổi.','Tổng hợp doanh thu theo tháng, sản phẩm và khu vực; kiểm tra doanh thu khớp nguồn.','Báo cáo Excel và 3 nhận xét có bằng chứng.','R01, R03'],
['4','SQL cơ bản','SELECT, WHERE, ORDER BY, DISTINCT, CASE; NULL và kiểu dữ liệu.','Lọc đơn theo thời gian, trạng thái, khu vực; giải thích cách xử lý NULL.','10 truy vấn có câu hỏi và kết quả mong đợi.','R04, R05, R06'],
['5','SQL tổng hợp','COUNT, SUM, AVG, GROUP BY, HAVING; lưu ý grain và đếm trùng.','Tính doanh thu, số đơn, giá trị trung bình mỗi đơn theo tháng và sản phẩm.','Bộ truy vấn KPI; đối chiếu ít nhất 2 kết quả với Excel.','R04, R05'],
['6','SQL kết hợp','INNER/LEFT JOIN, khóa, cardinality, UNION ALL, subquery, CTE.','Kết hợp orders, order items, customers; kiểm tra số dòng và tổng trước/sau JOIN.','Truy vấn có giải thích khóa và cách tránh nhân bản doanh thu.','R04, R05'],
['7','SQL nâng cao','Window functions: ROW_NUMBER, RANK, LAG, tổng lũy kế; ngày và tối ưu cơ bản.','Xếp hạng sản phẩm, tính tăng trưởng tháng và doanh thu lũy kế.','5 truy vấn có giải thích partition, thứ tự và xử lý tháng đầu.','R04, R05'],
['8','Thống kê mô tả','Mean, median, độ lệch chuẩn, phân vị, phân phối, outlier; mẫu và tổng thể.','So sánh mean/median giá trị đơn, vẽ phân phối; điều tra ngoại lệ trước khi loại.','Bảng thống kê, biểu đồ và giải thích giới hạn dữ liệu.','R07, R08'],
['9','Suy luận và kinh doanh','Lấy mẫu, xác suất, tương quan, confidence interval, kiểm định và A/B test cơ bản.','Thiết kế A/B test: giả thuyết, metric, nhóm, thời gian và rủi ro sai lệch.','Một trang kế hoạch thử nghiệm; phân biệt tương quan với quan hệ nhân quả.','R07, R08'],
['10','Power BI chuẩn bị dữ liệu','Power BI Desktop, Power Query, làm sạch, mô hình sao, quan hệ và grain.','Nhập bộ mẫu, tạo bảng fact/dimension, kiểm tra khóa và quan hệ.','Mô hình dữ liệu có sơ đồ và số liệu đối chiếu.','R09, R10, R11'],
['11','Power BI và DAX','Measure so với calculated column; SUM, DISTINCTCOUNT, CALCULATE, DIVIDE; filter context.','Tạo doanh thu, số đơn, AOV và so sánh kỳ; kiểm tra mẫu tính thủ công.','File PBIX với measure được giải thích và đối chiếu.','R09, R10'],
['12','Trực quan và trình bày','Chọn biểu đồ, thiết kế dashboard, bộ lọc, ngữ cảnh, kể chuyện bằng dữ liệu.','Tạo dashboard doanh thu theo thời gian, sản phẩm và khu vực; viết 3 insight và đề xuất.','Dashboard dễ đọc kèm định nghĩa KPI, phạm vi và hạn chế.','R09, R10, R11'],
['13','Python nền tảng','Biến, kiểu, list/dict, điều kiện, vòng lặp, hàm; môi trường notebook.','Đọc CSV, viết hàm kiểm tra giá trị thiếu và kiểu dữ liệu.','Notebook chạy từ đầu với mô tả từng bước.','R12, R13'],
['14','Python phân tích','pandas: read_csv, missing, duplicate, groupby, merge, pivot; NumPy cơ bản.','Làm sạch, kết hợp và tổng hợp dữ liệu; kiểm tra grain và tổng sau merge.','Notebook tái lập được, ghi rõ xử lý thiếu/trùng và các giả định.','R12, R14, R15'],
['15','Python trực quan và dự án','Matplotlib/Seaborn; phân tích xu hướng, ngoại lệ và viết kết luận.','Hoàn thành dự án Sales end-to-end; so sánh kết quả Python với SQL hoặc Excel.','Notebook, truy vấn, dashboard và README theo sheet Dự án.','R14, R15, R16'],
['16','Portfolio và ứng tuyển','Rà soát dự án, trình bày insight, SQL interview, CV và bảo mật dữ liệu.','Trình bày 5 phút: bài toán, cách làm, phát hiện, hạn chế, đề xuất; luyện giải thích JOIN/KPI.','1 dự án hoàn chỉnh công khai bằng dữ liệu được phép; kế hoạch bổ sung dự án 2.','R16, R17']
];
road.getRange('A2').values=[['LỘ TRÌNH HỌC DATA ANALYST · 16 TUẦN']];
road.getRange('A3').values=[['Giả định: người mới, 8–10 giờ/tuần (~128–160 giờ); điều chỉnh theo nền tảng và thời gian thực tế.']];
road.getRange('A4').values=[['Cách dùng: học theo thứ tự, làm bài tập và đối chiếu đầu ra; tra mã R tại sheet Tài nguyên. Đánh dấu tiến độ ở cột G.']];
road.getRange('A6:G6').values=[['Tuần','Chủ đề','Kiến thức trọng tâm','Bài tập thực hành','Đầu ra / tiêu chí hoàn thành','Nguồn','Tiến độ']];
road.getRange('A7:G22').values=weeks.map(x=>[...x,'Chưa bắt đầu']);
road.getRange('G7:G22').dataValidation={rule:{type:'list',values:['Chưa bắt đầu','Đang học','Hoàn thành']}};
const resources=[
['R01','Excel','Hướng dẫn Excel','Tiếng Việt','Tài liệu','Miễn phí đọc; ứng dụng Excel có thể cần giấy phép','https://support.microsoft.com/vi-vn/excel'],
['R02','Excel','Excel help & learning','English','Tài liệu','Miễn phí đọc; ứng dụng Excel có thể cần giấy phép','https://support.microsoft.com/en-us/excel'],
['R03','Power Query','Giới thiệu Power Query','Tiếng Việt','Tài liệu','Miễn phí đọc; phần mềm tùy phiên bản','https://learn.microsoft.com/vi-vn/power-query/power-query-what-is-power-query'],
['R04','SQL','Get started querying with Transact-SQL','Tiếng Việt','Lộ trình tự học','Nội dung Microsoft Learn miễn phí; môi trường thực hành có thể có yêu cầu riêng','https://learn.microsoft.com/vi-vn/training/paths/get-started-querying-with-transact-sql/'],
['R05','SQL','SQLBolt','English','Khóa tương tác','Miễn phí học trên web','https://sqlbolt.com/'],
['R06','SQL','SQL Server căn bản – Kteam','Tiếng Việt','Khóa học','Nội dung khóa miễn phí; kiểm tra điều kiện đăng nhập trên trang','https://www.howkteam.vn/course/khoa-hoc-sql-server-can-ban-20'],
['R07','Thống kê','Statistics and probability – Khan Academy','English','Khóa học','Miễn phí học; tài khoản cần cho lưu tiến độ','https://www.khanacademy.org/math/statistics-probability'],
['R08','Thống kê','OpenIntro Statistics','English','Sách','PDF miễn phí; bản in có phí','https://www.openintro.org/book/os/'],
['R09','Power BI','Get started with Microsoft data analytics','Tiếng Việt','Lộ trình tự học','Nội dung Learn miễn phí; Power BI Service có tính năng cần giấy phép','https://learn.microsoft.com/vi-vn/training/paths/get-started-power-bi/'],
['R10','Power BI','Microsoft Learn Power BI','English','Khóa và tài liệu','Nội dung Learn miễn phí; dịch vụ và chứng chỉ có thể có phí','https://learn.microsoft.com/en-us/training/powerplatform/power-bi/'],
['R11','Power BI','Bộ dữ liệu mẫu Power BI','English','Dữ liệu thực hành','Miễn phí tải; kiểm tra quyền sử dụng của từng bộ','https://learn.microsoft.com/en-us/power-bi/create-reports/sample-datasets'],
['R12','Python','Lập trình Python cơ bản – Kteam','Tiếng Việt','Khóa học','Nội dung khóa miễn phí; kiểm tra điều kiện đăng nhập trên trang','https://www.howkteam.vn/course/lap-trinh-python-co-ban-21'],
['R13','Python','Python Tutorial – W3Schools','English','Khóa / tài liệu','Bài học miễn phí; dịch vụ bổ sung có thể tính phí','https://www.w3schools.com/python/'],
['R14','Python','pandas introductory tutorials','English','Tài liệu và bài tập','Miễn phí','https://pandas.pydata.org/docs/getting_started/intro_tutorials/'],
['R15','Python','Data Analysis with Python – freeCodeCamp','English','Khóa học','Miễn phí; điều kiện chứng nhận xem trên trang','https://www.freecodecamp.org/learn/data-analysis-with-python/'],
['R16','Dữ liệu','Kaggle Datasets','English','Bộ dữ liệu','Duyệt miễn phí; tài khoản có thể cần để tải; kiểm tra license từng dataset','https://www.kaggle.com/datasets'],
['R17','Thực hành','Kaggle Learn','English','Khóa thực hành','Các bài học miễn phí; có thể cần tài khoản','https://www.kaggle.com/learn'],
['R18','SQL','SQL Tutorial – W3Schools','English','Khóa / tài liệu','Bài học miễn phí; dịch vụ bổ sung có thể tính phí','https://www.w3schools.com/sql/'],
['R19','SQL','SQL Server căn bản – Kteam','Tiếng Việt','Khóa học','Học theo bài; cú pháp là SQL Server, có thể khác PostgreSQL/MySQL','https://www.howkteam.vn/course/khoa-hoc-sql-server-can-ban-20']
];
res.getRange('A2').values=[['TÀI NGUYÊN HỌC VÀ THỰC HÀNH']];
res.getRange('A3').values=[['Chọn một nguồn chính cho mỗi chủ đề; URL có thể sao chép vào trình duyệt. Điều kiện truy cập và giá có thể thay đổi. Kiểm tra ngày 03/11/2026.']];
res.getRange('A5:G5').values=[['Mã','Chủ đề','Tên tài nguyên','Ngôn ngữ','Loại','Chi phí / điều kiện','Đường dẫn']];
res.getRange('A6:G24').values=resources;
const projects=[
['1 · Sales Performance','Ưu tiên hoàn thành','Doanh thu giảm ở tháng, chi nhánh hay sản phẩm nào? Số đơn hay AOV thay đổi?','Doanh thu = tổng doanh thu dòng hợp lệ; số đơn = distinct order_id; AOV = doanh thu / số đơn. Thống nhất hoàn trả, thuế và kỳ.','Excel + SQL + Power BI; Python bổ sung','Báo cáo xu hướng, phân rã theo nhóm; đối chiếu tổng và đề xuất có căn cứ.'],
['2 · Customer Behavior','Sau dự án 1','Khách mới và khách quay lại thay đổi thế nào? Nhóm nào có tỷ lệ mua lại cao?','Định nghĩa khách, kỳ theo dõi, repeat purchase rate; cohort/retention cần dữ liệu theo thời gian đủ dài.','SQL + Python + Power BI','Cohort và phân khúc nếu dữ liệu cho phép; ghi rõ giới hạn quan sát.'],
['3 · HR Analytics','Tùy chọn','Tỷ lệ nghỉ việc khác nhau giữa nhóm nào và dữ liệu có hạn chế gì?','Turnover = số người nghỉ trong kỳ / nhân sự bình quân kỳ; định nghĩa population và kỳ.','Excel + SQL + Power BI; Python bổ sung','Báo cáo tổng hợp, tránh công bố thông tin cá nhân hoặc suy luận nguyên nhân khi thiếu chứng cứ.']
];
proj.getRange('A2').values=[['DỰ ÁN THỰC HÀNH VÀ PORTFOLIO']];
proj.getRange('A3').values=[['Chọn 1 dự án hoàn chỉnh trước; dữ liệu công khai hoặc được phép sử dụng. Không đăng dữ liệu cá nhân, bí mật hay thông tin nhạy cảm.']];
proj.getRange('A5:F5').values=[['Dự án','Ưu tiên','Câu hỏi gợi ý','KPI / lưu ý','Công cụ','Đầu ra']];
proj.getRange('A6:F8').values=projects;
proj.getRange('A11').values=[['CHECKLIST CHO MỖI DỰ ÁN']];
proj.getRange('A12:B20').values=[['1','Mô tả bài toán, đối tượng đọc, phạm vi và câu hỏi.'],['2','Ghi nguồn, license, thời điểm, phạm vi và data dictionary.'],['3','Ghi chất lượng dữ liệu, missing/duplicate, cách xử lý và giả định.'],['4','Lưu truy vấn / notebook, mô hình dữ liệu và hướng dẫn chạy lại.'],['5','Định nghĩa KPI, kiểm tra grain, JOIN và đối chiếu tổng.'],['6','Tạo dashboard hoặc biểu đồ phù hợp; ghi bộ lọc và kỳ phân tích.'],['7','Viết 3 insight có số liệu, đề xuất khả thi và giới hạn kết luận.'],['8','Viết README: mục tiêu, dữ liệu, công cụ, cách chạy và kết quả.'],['9','Kiểm tra dữ liệu được phép chia sẻ; ẩn danh và tránh secrets trước khi công khai.']];
road.mergeCells('A2:G2'); road.mergeCells('A3:G3'); road.mergeCells('A4:G4');
res.mergeCells('A2:G2'); res.mergeCells('A3:G3');
proj.mergeCells('A2:F2'); proj.mergeCells('A3:F3');
for (const sh of [road,res,proj]) {
 sh.showGridLines=false;
 const n=sh===road?22:sh===res?24:20;
 sh.getRange(`A1:G${n}`).format.font={name:'Arial',size:10,color:'#203047'};
 sh.getRange(`A1:G${n}`).format.verticalAlignment='top';
 sh.getRange(`A1:G${n}`).format.wrapText=true;
 sh.getRange('A2:G2').format.font={name:'Arial',size:14,bold:true,color:'#17365D'};
 sh.getRange('A3:G4').format.font={name:'Arial',size:10,italic:true,color:'#53677E'};
 const header=sh===road?'A6:G6':sh===res?'A5:G5':'A5:F5';
 sh.getRange(header).format={fill:'#234B70',font:{name:'Arial',size:10,bold:true,color:'#FFFFFF'},rowHeight:28,wrapText:true,verticalAlignment:'center'};
 sh.freezePanes.freezeRows(sh===road?6:5);
}
road.getRange('A2:G2').format.rowHeight=28; road.getRange('A3:G4').format.rowHeight=22;
res.getRange('A2:G2').format.rowHeight=28; res.getRange('A3:G3').format.rowHeight=28;
proj.getRange('A2:F2').format.rowHeight=28; proj.getRange('A3:F3').format.rowHeight=28;
for(const [c,w] of Object.entries({A:9,B:22,C:43,D:53,E:48,F:16,G:18}))road.getRange(`${c}:${c}`).format.columnWidth=w;
road.getRange('A7:G22').format.rowHeight=83; road.getRange('G7:G22').format.fill='#FFF3CC';
for(const [c,w] of Object.entries({A:9,B:18,C:38,D:16,E:19,F:51,G:65}))res.getRange(`${c}:${c}`).format.columnWidth=w;
res.getRange('A6:G24').format.rowHeight=50;
for(const [c,w] of Object.entries({A:27,B:21,C:53,D:58,E:29,F:58}))proj.getRange(`${c}:${c}`).format.columnWidth=w;
proj.getRange('A6:F8').format.rowHeight=105; proj.getRange('A12:B20').format.rowHeight=32; proj.getRange('A11:F11').format.font={name:'Arial',size:11,bold:true,color:'#234B70'};
wb.recalculate();
for(const [sh,range,file] of [[road,'A1:G9','road.png'],[res,'A1:G8','resources.png'],[proj,'A1:F8','projects.png']]){
 const blob=await wb.render({sheetName:sh.name,range,scale:1,format:'png'}); await fs.writeFile(`${out}/${file}`,new Uint8Array(await blob.arrayBuffer()));
}
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A',options:{useRegex:true,maxResults:50}})).ndjson);
const file=await SpreadsheetFile.exportXlsx(wb); await file.save(`${out}/Lo-trinh-hoc-Data-Analyst.xlsx`);
