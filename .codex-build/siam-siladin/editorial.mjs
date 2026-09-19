import fs from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {Presentation,PresentationFile} from '@oai/artifact-tool';
const root='D:/VS_Project/MiniProject_Everything_1';
const skill='C:/Users/Admin/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.11809/skills/presentations';
const {finalizePresentation}=await import(pathToFileURL(skill+'/container_tools/artifact_tool_utils.mjs'));
const p=Presentation.create({slideSize:{width:1280,height:720}});
const ink='#352B26', clay='#A45138', cream='#F5F0E7', gold='#AD8A57', mute='#796C60', olive='#344A40';
const font='Leelawadee';
function box(s,x,y,w,h,fill,line='none'){return s.shapes.add({geometry:'rect',position:{left:x,top:y,width:w,height:h},fill,line:{fill:line,width:line==='none'?0:1}})}
function t(s,txt,x,y,w,h,size=25,color=ink,bold=false,align='left',family=font){const a=box(s,x,y,w,h,'none');a.text=txt;a.text.style={typeface:family,fontSize:size,color,bold,alignment:align,autoFit:'none',wrap:'square',lineSpacing:1.15,insets:{left:0,right:0,top:0,bottom:0}};return a;}
function base(n,bg=cream){const s=p.slides.add();s.background.fill=bg;const c=bg===olive?'#D9D1BF':mute;t(s,'SIAM SILADIN',64,29,400,24,15,c);t(s,String(n).padStart(2,'0')+' / 05',1110,29,106,24,15,c,false,'right');box(s,64,66,1152,1,bg===olive?'#69786B':'#CFC3B2');return s;}
function title(s,txt,color=ink){t(s,txt,64,92,1150,66,44,color,true);}
function blank(s,x,y,w,h,label,dark=false){box(s,x,y,w,h,dark?'#42564B':'#E8E0D3',dark?'#7F8D7B':'#C7BAA4');t(s,label,x+22,y+h/2-18,w-44,36,19,dark?'#B6C0B1':'#958675',false,'center');}
// Cover: large wordmark and deliberate image reserve.
{
const s=base(1);box(s,734,67,546,653,olive);
t(s,'สยามศิลาดิน',63,124,680,115,76,ink,true);
t(s,'ธุรกิจเครื่องปั้นดินเผาที่สืบสานศิลปะ\nและวัฒนธรรมไทย',68,255,600,85,29,clay);
box(s,68,379,48,2,gold);
t(s,'“ปั้นดินให้มีคุณค่า\nเพราะงานศิลป์ไทยควรอยู่คู่คนไทย”',68,405,610,90,30,olive,true);
t(s,'สมาชิก',68,535,150,30,18,mute,true);
t(s,'นาย นราวิชญ์ ทิดมนตรี  ชั้น 2/7  เลขที่ 14\nนาย คณธัช หุ่นเมืองปัก  ชั้น 2/7  เลขที่ 15\nนาย นวพรรต ลีนะกิตติ  ชั้น 2/7  เลขที่ 20\nนาย ชลพล ศรีชาเยช  ชั้น 2/7  เลขที่ 21',68,578,605,115,18,ink);
blank(s,780,127,452,506,'พื้นที่สำหรับรูปหน้าปก',true);
}
// Origin: editorial feature with a separate brand meaning column.
{
const s=base(2);title(s,'แนวคิดและที่มาของธุรกิจ');
t(s,'แรงบันดาลใจ',64,208,470,42,29,clay,true);
t(s,'ศิลปะจากดิน\nที่อยู่คู่ชีวิตไทย',60,270,590,155,55,olive,true);
t(s,'พวกเราชื่นชอบศิลปะและวัฒนธรรมไทย โดยเฉพาะ\nเครื่องปั้นดินเผาที่สวยงาม เรียบง่าย และสะท้อน\nภูมิปัญญาท้องถิ่น จึงต้องการพัฒนางานหัตถกรรมไทย\nให้เข้ากับความสนใจของผู้บริโภคยุคใหม่',64,451,594,166,24,ink);
box(s,693,205,1,413,'#CFC3B2');
t(s,'ความหมายของชื่อ “สยามศิลาดิน”',741,207,475,43,27,ink,true);
t(s,'สยาม',741,278,170,43,30,clay,true);t(s,'สื่อถึงความเป็นไทย',741,326,475,36,23,mute);
t(s,'ศิลาและดิน',741,384,400,43,30,clay,true);t(s,'สื่อถึงวัสดุธรรมชาติและงานฝีมือ',741,432,475,36,23,mute);
t(s,'แนวคิดของธุรกิจ',741,497,475,36,25,olive,true);
t(s,'จำหน่ายเครื่องปั้นดินเผา ควบคู่ชุดระบายสี\nและคอร์สปั้นดิน ลูกค้าจึงได้ทั้งสินค้า ความรู้\nและความสนุกจากการลงมือทำ',741,543,475,105,23,ink);
box(s,64,656,1152,1,'#CFC3B2');t(s,'เป้าหมายของแบรนด์คือทำให้งานศิลป์จากดินเข้าถึงง่ายและอยู่ร่วมกับชีวิตประจำวัน',64,672,1152,31,20,olive);
}
// Audience: rows with prominent numbering, contrasted channel section.
{
const s=base(3);title(s,'กลุ่มเป้าหมายและช่องทางจัดจำหน่าย');
t(s,'กลุ่มเป้าหมายหลัก',64,200,565,40,27,clay,true);
const rows=[['01','ผู้ที่สนใจศิลปะและวัฒนธรรมไทย'],['02','นักเรียน นักศึกษา และครอบครัว\nที่มองหากิจกรรมสร้างสรรค์'],['03','นักท่องเที่ยวที่ต้องการของฝาก\nที่มีเอกลักษณ์ไทย'],['04','ผู้ที่ต้องการของตกแต่งบ้าน\nหรือของขวัญทำมือ']];
rows.forEach(([num,txt],i)=>{let y=264+i*78;t(s,num,64,y,60,39,27,gold);t(s,txt,145,y,480,65,24,ink);});
box(s,695,199,521,398,olive);
t(s,'ช่องทางจัดจำหน่าย',728,226,450,45,29,cream,true);
t(s,'หน้าร้าน',728,297,430,36,24,'#D8BC89',true);
t(s,'ร้านสยามศิลาดิน ตลาดงานฝีมือ\nและบูธตามเทศกาลหรือแหล่งท่องเที่ยว\nเชิงวัฒนธรรม',728,342,445,99,23,cream);
t(s,'ออนไลน์',728,464,430,34,24,'#D8BC89',true);
t(s,'Facebook, Instagram, TikTok\nLINE Official Account, Shopee, Lazada',728,511,450,65,22,cream);
box(s,64,625,1152,1,'#CFC3B2');
t(s,'เครื่องปั้นดินเผาเป็นทั้งของใช้ ของตกแต่ง และผลงานศิลปะ ลูกค้าจึงเลือกซื้อเพื่อใช้งาน สะสม\nมอบเป็นของขวัญ หรือเรียนรู้การปั้นด้วยตนเองได้',64,646,1152,64,23,olive);
}
// Products: clean catalogue with oversized category indexes.
{
const s=base(4,olive);title(s,'สินค้าและบริการ',cream);
const cats=[['01','เครื่องใช้และของตกแต่ง','ถ้วย จาน และหม้อ\n\nแจกันและของตกแต่งบ้าน\n\nกำไลข้อมือจากดินเผา'],['02','กิจกรรมสร้างสรรค์','ของเล่นจากดินเผา\n\nชุดเครื่องปั้นดินเผา\nสำหรับระบายสี\n\nอุปกรณ์ขึ้นรูปและตกแต่ง'],['03','คอร์สและประสบการณ์','คอร์สปั้นดินสำหรับผู้เริ่มต้น\n\nกิจกรรมสำหรับคู่รัก ครอบครัว\nและกลุ่มเพื่อน\n\nกิจกรรมสำหรับโรงเรียน\nและองค์กร']];
cats.forEach(([n,head,txt],i)=>{let x=64+i*396;t(s,n,x,190,210,100,76,'#B9A17A',false,'left','Georgia');t(s,head,x,306,366,44,27,cream,true);box(s,x,372,340,1,'#71806F');t(s,txt,x,397,365,214,23,cream);});
box(s,64,631,1152,1,'#71806F');t(s,'จุดเด่นของร้าน: ลูกค้าสามารถซื้อสินค้าสำเร็จรูป ออกแบบผลงานของตนเอง\nหรือเรียนรู้กระบวนการปั้นดินได้ภายในร้านเดียว',64,652,1152,62,23,'#D8BC89');
}
// Poster: blank landscape artwork and strong typographic offer.
{
const s=base(5);t(s,'สยามศิลาดิน',64,99,1152,70,51,ink,true);
t(s,'ทุกชิ้นมีเรื่องราว\nทุกการปั้นช่วยสืบสานงานศิลป์ไทย',804,108,412,60,21,clay,false,'right');
t(s,'Facebook, TikTok, LINE, QR Code',804,182,412,30,17,mute,false,'right');
t(s,'“ปั้นดินให้มีคุณค่า เพราะงานศิลป์ไทยควรอยู่คู่คนไทย”',67,176,1140,44,27,olive);
t(s,'สัมผัสเสน่ห์\nเครื่องปั้นดินเผาไทย',64,262,500,105,38,clay,true);
t(s,'เลือกซื้อของใช้ ของตกแต่ง และสนุกกับ\nกิจกรรมระบายสีหรือปั้นดินด้วยตนเอง',67,387,490,73,24,ink);
t(s,'โปรโมชั่นพิเศษ',67,491,420,35,23,olive,true);
t(s,'100',58,527,255,115,92,clay,true,'left','Georgia');
t(s,'บาท',299,579,180,52,35,clay,true);
t(s,'ซื้อครบ รับส่วนลดสินค้าชิ้นถัดไป 20 บาท',67,646,502,38,23,ink);
blank(s,576,242,640,360,'พื้นที่สำหรับรูปโปสเตอร์สินค้า 16:9');
t(s,'ซื้อชุดระบายสี 2 ชุดขึ้นไป\nรับส่วนลดคอร์สปั้นดิน 100 บาท',595,625,621,67,25,olive,true);
s.speakerNotes.textFrame.setText('ข้อความปิดท้ายสำหรับโปสเตอร์: ทุกชิ้นมีเรื่องราว ทุกการปั้นช่วยสืบสานงานศิลป์ไทย\nช่องทางติดต่อสำหรับเติมเมื่อพร้อม: Facebook, TikTok, LINE และ QR Code');
}
const candidate=root+'/.codex-finalizer/editorial-candidate.pptx';
await (await PresentationFile.exportPptx(p)).save(candidate);
await finalizePresentation({workspaceDir:root,candidatePath:candidate,finalPath:root+'/output/สยามศิลาดิน_กรอบโปสเตอร์16-9.pptx',pythonExecutable:'C:/Users/Admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',integrityValidatorPath:skill+'/container_tools/inspect_presentation_package_integrity.py',layoutValidatorPath:skill+'/container_tools/inspect_presentation_layout_geometry.py',layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-heading-fit'],explicitTotalSlideCount:5,fontPolicy:{basis:'design',families:[font,'Georgia']},verifyArtifactToolImport:true,receiptPath:root+'/.codex-finalizer/editorial-validation-16-9.json'});
console.log('Created editorial deck');
