export const photoPng=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=','base64');
export const libraryPhoto=(id,name,extra={})=>({id,originalFileName:name,processingStatus:'ready',ownerService:'catalog',ownerType:'product',concurrencyVersion:3,contentType:'image/png',sizeBytes:68,uploadedAtUtc:'2026-11-03T00:00:00Z',...extra});
export async function fulfillPhotoLibrary(route,photos){
 const url=new URL(route.request().url());
 if(url.pathname.startsWith('/bff/customer/media-library/assets/')){await route.fulfill({contentType:'image/png',body:photoPng});return true;}
 if(url.pathname!=='/bff/customer/media-library')return false;
 const query=url.searchParams.get('search')?.toLocaleLowerCase()??'',ready=url.searchParams.get('readyOnly')==='true',page=Number(url.searchParams.get('page')??1);
 const rows=photos.filter(a=>a.ownerService==='catalog'&&a.ownerType==='product'&&(!ready||a.processingStatus==='ready')&&a.originalFileName.toLocaleLowerCase().includes(query));
 await route.fulfill({json:{items:rows.slice((page-1)*24,page*24),hasMore:rows.length>page*24,page}});return true;
}
export async function pickPhoto(page,name){
 await page.getByRole('button',{name:'Choose from photo library',exact:true}).click();
 const dialog=page.getByRole('dialog',{name:'Choose a photo'});
 await dialog.getByRole('button',{name:`Select photo ${name}`,exact:true}).click();
 await dialog.getByRole('button',{name:'Use selected photo',exact:true}).click();
}
