// Verify what low-privilege users actually SEE on the flagged Organization pages.
// Confirms whether HTTP 200 = real privileged content (escalation) or a scoped/empty page.
const { chromium } = require('@playwright/test');
const BASE = process.env.APP_URL || 'http://localhost:5000';
const TARGETS = ['/Admin/Organization', '/Admin/Organization/Hierarchy', '/Admin/Organization/Grants'];
const USERS = {
  Owner:    { email:'admin@local',       password:'admin123' },
  NoGrants: { email:'nogrants@test',     password:'Test1234!' },
  Employee: { email:'emp.tz.alhut@test', password:'Test1234!' },
  Trainee:  { email:'trainee.alhut@test',password:'Test1234!' },
};
async function login(ctx,u){const p=await ctx.newPage();await p.goto(BASE+'/Auth/Login',{waitUntil:'domcontentloaded'});await p.fill('input[name="Email"]',u.email);await p.fill('input[name="Password"]',u.password);await Promise.all([p.waitForURL(x=>!x.toString().includes('/Auth/Login'),{timeout:8000}).catch(()=>{}),p.locator('form:has(input[name="Email"]) button[type="submit"]').click()]);await p.close();}
(async()=>{
  const b=await chromium.launch({headless:true});
  for(const [name,u] of Object.entries(USERS)){
    const ctx=await b.newContext({baseURL:BASE});await login(ctx,u);const p=await ctx.newPage();
    console.log(`\n############## ${name} (${u.email}) ##############`);
    for(const t of TARGETS){
      let st=0,url='';try{const r=await p.goto(BASE+t,{waitUntil:'domcontentloaded',timeout:15000});st=r?r.status():0;url=p.url();}catch(e){console.log(`  ${t} ERR ${e.message.slice(0,40)}`);continue;}
      const info=await p.evaluate(()=>{
        const title=document.title;
        const h1=Array.from(document.querySelectorAll('h1,h2')).slice(0,3).map(e=>e.innerText.trim()).join(' | ');
        const tableRows=document.querySelectorAll('table tbody tr').length;
        const selectOpts=document.querySelectorAll('select option').length;
        // sensitive action buttons / forms
        const actionBtns=Array.from(document.querySelectorAll('button,a.btn,input[type=submit]')).map(e=>e.innerText.trim()||e.value||'').filter(Boolean).slice(0,12);
        const forms=document.querySelectorAll('form').length;
        const bodyLen=document.body.innerText.length;
        const accessDenied=/access denied|אין הרשאה|not authorized|אין לך/i.test(document.body.innerText);
        return {title,h1,tableRows,selectOpts,actionBtns,forms,bodyLen,accessDenied};
      });
      console.log(`  ${t}`);
      console.log(`     HTTP ${st} -> ${url.replace(BASE,'')}`);
      console.log(`     title="${info.title}"  h="${info.h1}"`);
      console.log(`     tableRows=${info.tableRows} selectOpts=${info.selectOpts} forms=${info.forms} bodyLen=${info.bodyLen} accessDeniedText=${info.accessDenied}`);
      console.log(`     buttons=[${info.actionBtns.join(', ')}]`);
    }
    await ctx.close();
  }
  await b.close();
})();
